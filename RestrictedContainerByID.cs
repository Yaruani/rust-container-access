using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System;
using UnityEngine;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("Restricted Container By ID", "Yaruani", "1.8.16")]
    [Description("コンテナのスキンIDに基づく制限 (DepositStation連携版)")]
    class RestrictedContainerByID : RustPlugin
    {
        // ★連携: DepositStationプラグインへの参照
        [PluginReference]
        private Plugin DepositStation;

        private PluginConfig _config;

        #region Config
        private class PluginConfig
        {
            [JsonProperty("1. Enable Container Rules (箱側の制限を有効にする)")]
            public bool EnableContainerRules { get; set; } = false;

            [JsonProperty("1. Container Rules (Prefab -> {SkinID -> Allowed Items})")]
            public Dictionary<string, Dictionary<string, List<string>>> ContainerRules { get; set; } = new Dictionary<string, Dictionary<string, List<string>>>
            {
                { "box.wooden.large", new Dictionary<string, List<string>> { { "0", new List<string> { "wood", "stones", "metal.fragments" } } } }
            };

            [JsonProperty("2. Enable Item Rules (アイテム側の制限を有効にする)")]
            public bool EnableItemRules { get; set; } = false;

            [JsonProperty("2. Item Rules (アイテムのShortname -> 許可する箱のリスト)")]
            public Dictionary<string, List<string>> ItemRules { get; set; } = new Dictionary<string, List<string>>
            {
                { "scrap", new List<string> { "cupboard.tool", "cupboard.tool.deployed" } }
            };

            [JsonProperty("3. Container Display Name Map (箱のPrefab名 -> 表示名)")]
            public Dictionary<string, string> ContainerDisplayNames { get; set; } = new Dictionary<string, string>
            {
                { "box.wooden.large", "大型木箱" },
                { "cupboard.tool", "ツール・ボックス (TC)" }
            };

            [JsonProperty("4. Enable Discord Webhook")]
            public bool EnableDiscordWebhook { get; set; } = false;

            [JsonProperty("4. Discord Webhook URL (ログ送信先)")]
            public string DiscordWebhookUrl { get; set; } = "YOUR_DISCORD_WEBHOOK_URL_HERE";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Config is null");
            }
            catch (Exception ex)
            {
                PrintWarning($"設定ファイルの読み込みに失敗しました: {ex.Message}。デフォルト設定を作成します。");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
        }
        #endregion

        #region Helpers
        private string FormatItemName(string shortname)
        {
            var itemDef = ItemManager.FindItemDefinition(shortname);
            return itemDef?.displayName.english ?? shortname;
        }

        private string FormatContainerName(string prefabName)
        {
            if (_config.ContainerDisplayNames != null)
            {
                if (_config.ContainerDisplayNames.TryGetValue(prefabName, out string displayName)) return displayName;
                
                string cleanName = prefabName.Replace(".deployed", "");
                if (_config.ContainerDisplayNames.TryGetValue(cleanName, out displayName)) return displayName;
            }
            return prefabName;
        }

        private void SendDiscordWebhook(string message, string reason)
        {
            if (!_config.EnableDiscordWebhook || string.IsNullOrEmpty(_config.DiscordWebhookUrl) || _config.DiscordWebhookUrl.Contains("YOUR_DISCORD")) return;

            var payload = new
            {
                username = "Rust Restriction Bot",
                content = $"**[{reason} DENY]** {message}"
            };

            webrequest.Enqueue(_config.DiscordWebhookUrl, JsonConvert.SerializeObject(payload), (code, response) =>
            {
                if (code < 200 || code >= 300)
                {
                    PrintWarning($"Discord Webhook送信エラー。コード: {code}");
                }
            }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }
        #endregion

        #region Core Hook
        object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            // 基本的なNullチェック
            if (item == null || container == null) return null;
            
            BaseEntity entity = container.entityOwner;
            // エンティティまたはネットワークIDが無効な場合は処理しない
            if (entity == null || entity.net == null) return null;

            // ---------------------------------------------------------
            // 0. DepositStation 連携チェック
            // ---------------------------------------------------------
            if (DepositStation != null && DepositStation.IsLoaded)
            {
                // DepositStationプラグインの IsDepositStation フックを呼び出す
                var result = DepositStation.Call("IsDepositStation", entity.net.ID.Value);
                if (result is bool isDs && isDs)
                {
                    // DepositStationなら制限を適用せず許可
                    return null;
                }
            }

            // プレイヤーによる操作でなければ無視（NPCや自動化設備など）
            var player = item.GetOwnerPlayer();
            if (player == null) return null;

            string containerName = entity.ShortPrefabName;
            string itemName = item.info.shortname;
            string cleanContainerName = containerName.Replace(".deployed", "");

            // ---------------------------------------------------------
            // 1. パターンA: 箱側のルールチェック (ContainerRules)
            // ---------------------------------------------------------
            if (_config.EnableContainerRules)
            {
                Dictionary<string, List<string>> skinRules;
                
                // Prefab名でルールを検索
                if (_config.ContainerRules.TryGetValue(containerName, out skinRules) || 
                    _config.ContainerRules.TryGetValue(cleanContainerName, out skinRules))
                {
                    string skinId = entity.skinID.ToString();
                    List<string> allowedItems;

                    // SkinIDごとの許可リストを取得 (特定のスキンID -> デフォルト"0")
                    if (skinRules.TryGetValue(skinId, out allowedItems) || 
                        skinRules.TryGetValue("0", out allowedItems))
                    {
                        if (!allowedItems.Contains("*") && !allowedItems.Contains(itemName))
                        {
                            string fItem = FormatItemName(itemName);
                            string fCont = FormatContainerName(containerName);
                            string log = $"{player.displayName} ({player.UserIDString}) が {fCont} (Skin: {skinId}) に {fItem} ({itemName}) を入れようとしましたが、Container Ruleにより拒否されました。";
                            
                            Puts(log);
                            SendDiscordWebhook(log, "CR");

                            string allowedListString = string.Join(", ", allowedItems.Select(FormatItemName));
                            SendReply(player, $"<color=orange>[制限]</color> {fCont} (Skin: {skinId}) に {fItem} は入れられません。\n許可リスト: <color=#add8e6>{allowedListString}</color>");
                            
                            return ItemContainer.CanAcceptResult.CannotAccept;
                        }
                    }
                }
            }

            // ---------------------------------------------------------
            // 2. パターンB: アイテム側のルールチェック (ItemRules)
            // ---------------------------------------------------------
            if (_config.EnableItemRules && _config.ItemRules.TryGetValue(itemName, out var allowedContainers))
            {
                bool isAllowed = allowedContainers.Contains(containerName) || allowedContainers.Contains(cleanContainerName);

                if (!isAllowed)
                {
                    string fItem = FormatItemName(itemName);
                    string fCont = FormatContainerName(containerName);
                    string log = $"{player.displayName} ({player.UserIDString}) が {fItem} ({itemName}) を {fCont} に入れようとしましたが、Item Ruleにより拒否されました。";
                    
                    Puts(log);
                    SendDiscordWebhook(log, "IR");

                    string allowedListString = string.Join(", ", allowedContainers.Select(FormatContainerName));
                    SendReply(player, $"<color=red>[重要]</color> {fItem} は指定された保管庫にしか入れられません。\n現在選択中の箱: {fCont} / 許可された箱: <color=#add8e6>{allowedListString}</color>");
                    
                    return ItemContainer.CanAcceptResult.CannotAccept;
                }
            }
            
            return null;
        }
        #endregion
    }
}
