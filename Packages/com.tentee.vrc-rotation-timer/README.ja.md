# Rotation Alert

Rotation Alert は、交流会・展示回遊・グループトークなど、ローテーション制イベント向けの
同期タイマーとアラートパネルです。ローテ開始・終了予告・終了を短いローカルチャイムで知らせ、
ワールド側の演出から利用できるイベントと導出状態も提供します。

## 必要環境

- Unity 2022.3
- VRChat SDK Worlds 3.x
- VRChat Worlds SDK に含まれる UdonSharp
- TextMeshPro Essential Resources

`com.vrchat.worlds` `^3.8.0` に依存します。

## 導入

1. TenteEEEE の VPM listing から VCC 経由でパッケージを追加します。
2. パネルを置くシーンを開きます。
3. `Tools > TenteEEEE > Rotation Alert > Install Panel into Current Scene` を実行します。
4. 既存のパネルが無ければ `(0, 1.3, 0)` に配置されます。
5. 表示専用モニターを追加する場合は `Prefabs/RotationAlert Display.prefab` を置き、
   `RotationAlertDisplay.Core` にパネル側の `RotationAlertCore` を設定します。

生成済みの program asset と prefab を同梱しているため、通常のインストールでは Unity の
読み取り専用 PackageCache へ書き込みません。書き込み可能な embedded package または上流の
`Assets/RotationAlert` 作業用コピーでは、同じメニューの `Build Prefabs` で再生成できます。

## 機能

- ローテーション、休憩、回数、予告分数の設定。
- 開始、一時停止／再開、次へ、延長、短縮、2 段階確認リセット。
- 合図ごとの音源・音量・リピート回数と、ローカルミュート。
- `OnRotationStart`、`OnRotationWarning`、`OnRotationEnd`、`OnIntervalCountdown`、
  `OnAllFinished`、`OnScheduleChanged` の listener イベント。
- 任意で利用できる `_UdonRotAlertState` / `_UdonRotAlertFlags` シェーダー連携。

同期モデルと公開コンポーネントの仕様は [`docs/SPEC.md`](docs/SPEC.md) を参照してください。

## ライセンスとクレジット

コード、生成 UI 素材、オリジナル音声は `LICENSE` の MIT License で提供します。付属の Noto Sans JP
フォントは SIL Open Font License 1.1 です（`Fonts/OFL-1.1.txt`）。音声生成元のクレジットは README に記載しています。
