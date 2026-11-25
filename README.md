# RestrictedContainerByID

RustでコンテナのPrefab名・スキンIDやアイテムのShortnameに応じて保管先を制限するプラグインです。
DepositStation（開発中）と連携可。

## 導入

[RestrictedContainerByID.cs](RestrictedContainerByID.cs) をCarbonでは `carbon/plugins`、Oxideでは `oxide/plugins` に配置します。
設定ファイルは `carbon/configs/RestrictedContainerByID.json` または `oxide/config/RestrictedContainerByID.json` です。
箱側・アイテム側の制限は、初期設定ではどちらも無効です。必要なルールを設定して有効にし、プラグインを再ロードしてください。

## ルールの判定

1. **箱側**。箱の短いPrefab名とスキンIDで、投入できるアイテムのShortnameを指定します。個別のスキンIDに一致しない場合は `"0"` のルールを使用します。許可リストの `"*"` は箱側の判定を通過させます。
2. **アイテム側**。アイテムのShortnameごとに、投入できる箱の短いPrefab名を指定します。両方の制限が有効なら、箱側に `"*"` がある場合もアイテム側のルールを判定します。

箱のPrefab名は、そのままの名前と `.deployed` を除いた名前で照合します。アイテムの所有プレイヤーを取得できない場合は、このプラグインの制限を適用しません。

制限で拒否した操作はサーバーログに記録します。Discordにも送信する場合は `4. Enable Discord Webhook` を有効にして `4. Discord Webhook URL (ログ送信先)` を設定してください。

## DepositStationとの連携

DepositStationがロードされ、対象コンテナが登録済みであれば、`IsDepositStation` フックを使って判定し、そのコンテナをRestrictedContainerByIDの箱側・アイテム側の制限から除外します。ゲーム本体と他プラグインの制限、およびDepositStationのポイント付与ルールは、それぞれの実装に従います。
DepositStationが未ロードの場合は通常のルールを適用します。

## 設定例

```json
{
  "1. Enable Container Rules (箱側の制限を有効にする)": true,
  "1. Container Rules (Prefab -> {SkinID -> Allowed Items})": {
    "box.wooden.large": {
      "0": [ "wood", "stones", "metal.fragments" ],
      "123456789": [ "*" ]
    }
  },
  "2. Enable Item Rules (アイテム側の制限を有効にする)": true,
  "2. Item Rules (アイテムのShortname -> 許可する箱のリスト)": {
    "scrap": [ "cupboard.tool.deployed" ]
  }
}
```

個別ルールのないスキンの大木箱には、木材・石・金属片を投入できます。スキンID `123456789` の箱は箱側の判定を通過します。ただし、スクラップを投入できるのはツールカップボード（TC）だけです。DepositStationの登録済みコンテナは、このプラグインが適用する両方の判定から除外されます。

## ライセンス

[MIT License](LICENSE)
