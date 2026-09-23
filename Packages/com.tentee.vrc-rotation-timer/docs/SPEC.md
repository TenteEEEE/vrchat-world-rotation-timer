# Rotation Alert 仕様書

## 1. 目的

Rotation Alert は、交流会や展示回遊のように、参加者が一定時間ごとに移動する VRChat ワールド向けのタイマーです。ワールドにパネルを置くと、進行役が開始・一時停止・スキップなどを操作でき、参加者には現在のローテーションと残り時間が表示されます。

時間と状態は全員が同じサーバー時刻から計算します。カウントダウンの数字や音声そのものは同期せず、各クライアントが自分で表示・再生します。

対象環境は Unity 2022.3、VRChat Worlds SDK 3.8.x、UdonSharp、TextMeshPro 3.0.6 です。

## 2. スケジュール

| 用語 | 意味 | 初期値 |
| --- | --- | ---: |
| ローテーション | 参加者が活動する時間 | 15分 |
| インターバル | ローテーション間の移動・休憩時間 | 2分 |
| 回数 | ローテーションの回数 | 6回 |
| 予告 | ローテーション終了前の通知 | 3分前 |
| 開始前カウント | 次のローテーション開始前に外部連携へ送る通知 | 30秒前 |

スケジュールは次の順に進みます。

```text
[ローテーション1] [インターバル] [ローテーション2] ... [ローテーションN]
```

最後のローテーションが終わった時点で完了です。最後のローテーションの後にはインターバルを置きません。インターバルを 0 秒にすると、ローテーションを続けて実行できます。

内部では、ローテーション時間を `rotationSec`、インターバルを `intervalSec` とし、1 周を次のように扱います。

```text
cycle = rotationSec + intervalSec
```

経過時間から現在の状態を求めるときは、まず `cycle` で割ってローテーション番号を求めます。最終ローテーションの活動時間を過ぎていた場合は、インターバルではなく完了状態にします。

## 3. 合図と外部イベント

音声キューは次の 4 種類です。`r` は 0 始まりのローテーション番号です。

| 種類 | 発火時刻 | 既定の音声 |
| --- | --- | --- |
| RotationStart | `r * cycle` | `rotation_start.wav` |
| Warning | `r * cycle + rotationSec - warnSec` | `warning.wav` |
| RotationEnd | `r * cycle + rotationSec`（最後を除く） | `rotation_end.wav` |
| AllFinished | `(rotationCount - 1) * cycle + rotationSec` | `rotation_end.wav` |

Warning は `warnSec` が 0 より大きく、ローテーション時間より短い場合だけ発火します。

開始前カウントは、外部連携用のイベントとしてだけ通知します。配列は id 0〜4 に合わせて 5 要素で、id 3（Countdown）は意図的に null クリップのまま無音にします。各音声キューには、クリップ、音量、1〜3 回の繰り返し回数、繰り返し間隔を設定できます。初期の繰り返し間隔は 0.6 秒です。

音は `AudioSource.PlayOneShot` で各クライアントだけが再生します。ネットワークイベントで音を鳴らしてはいけません。途中参加者に過去の合図をまとめて鳴らすこともしません。初回検出時は直近 2 秒だけを拾い、それより前のキューは通過済みとして扱います。

同じフレームに複数の音声キューが重なった場合、外部イベントはすべて通知します。音声は AllFinished > RotationEnd > Warning > RotationStart の順で最も優先度の高いキューだけを再生します。Countdown は無音です。RotationEnd は最終ローテーションでは発火しないため、AllFinished と衝突しません。

外部連携先 `listeners` には、次のイベントを送ります。null の要素は無視します。

```text
OnRotationStart
OnRotationWarning
OnRotationEnd
OnIntervalCountdown
OnAllFinished
OnScheduleChanged
```

## 4. 同期と操作

スケジュールの本体は `RotationAlertCore` です。同期モードは Manual とし、同期するのは時刻と設定値だけにします。

```csharp
[UdonSynced] public bool running;
[UdonSynced] public double scheduleStart;
[UdonSynced] public double pausedAt;
[UdonSynced] public int rotationSec;
[UdonSynced] public int intervalSec;
[UdonSynced] public int rotationCount;
[UdonSynced] public int warnSec;
[UdonSynced] public int revision;
```

`scheduleStart` と `pausedAt` は `Networking.GetServerTimeInSeconds()` の値です。`pausedAt` が 0 のときは通常進行、0 より大きいときはその時刻で時間を止めます。`revision` は操作のたびに増やし、表示と音声が変更を検出するために使います。

Inspector には初期値と外部連携先を持たせます。

```csharp
public int defaultRotationMinutes = 15;
public int defaultIntervalMinutes = 2;
public int defaultRotationCount = 6;
public int defaultWarnMinutes = 3;
public int preStartSec = 30;
public UdonBehaviour[] listeners;
```

表示と音声は、次の導出メソッドを毎フレーム呼び出して状態を取得します。これらは計算だけを行い、同期状態を変更しません。

```csharp
public double EffectiveElapsed();
public int GetPhase();
public bool IsPaused();
public int GetRotationIndex();
public float GetRemaining();
public float GetPhaseLength();
public int Cycle();
```

`GetPhase()` の戻り値は、0 が待機、1 がローテーション、2 がインターバル、3 が完了です。一時停止はフェーズとは別に `IsPaused()` で確認します。

操作は public メソッドにし、UI から `SendCustomEvent` で呼び出します。操作前に所有権を取得し、状態を変更して `revision` を増やしたあと、`RequestSerialization()` を呼びます。

| 操作 | 条件 | 動作 |
| --- | --- | --- |
| `OpStart` | 待機中または完了後 | 現在時刻から新しいスケジュールを開始 |
| `OpPauseResume` | 実行中 | 一時停止、または停止位置から再開 |
| `OpSkip` | 実行中 | 4 秒以内の 2 回押下で現在のフェーズを終了 |
| `OpPlusMinute` | 実行中 | 現在のフェーズの残りを 60 秒延長 |
| `OpMinusMinute` | 実行中 | 現在のフェーズの残りを 60 秒短縮 |
| `OpResetRequest` | 常時 | 4 秒以内の 2 回押下で待機状態へ戻す |
| `OpRotationPlus/Minus` | 待機中 | ローテーション時間を 60 秒単位で変更 |
| `OpIntervalPlus/Minus` | 待機中 | インターバルを 60 秒単位で変更 |
| `OpCountPlus/Minus` | 待機中 | 回数を 1 回単位で変更 |
| `OpWarnPlus/Minus` | 待機中 | 予告時間を 60 秒単位で変更 |

設定値の範囲は、ローテーション 60〜3600 秒、インターバル 0〜1800 秒、回数 1〜30 回、予告 0〜`rotationSec - 60` 秒です。実行中は設定変更ボタンを無効にします。

残り時間の延長・短縮は、現在のフェーズの開始位置を基準に新しい残り時間を計算してから `scheduleStart` を更新します。単純に `scheduleStart` を何度も足し引きすると誤差が蓄積するため、Skip と同じ計算経路を使います。

## 5. 表示

`RotationAlertDisplay` はローカル専用の表示コンポーネントです。`RotationAlertCore` を参照し、次の内容を表示します。

| 表示 | 内容 |
| --- | --- |
| `phaseText` | 待機中、ローテーション番号、インターバル、一時停止中、全ローテ終了 |
| `timeText` | 残り時間を `mm:ss` で表示。秒は切り上げ |
| `subText` | 次の予定、予告、設定の概要、完了メッセージ |
| `progressFill` | 現在のフェーズの進捗 |
| `accentImages` | フェーズに応じた色の帯や枠 |

色は、待機中がグレー、ローテーション中がティール、予告中がアンバー、残り 60 秒以内が赤、インターバルがブルー、完了がグリーンです。一時停止中はグレーにし、残り時間を点滅させます。

操作パネルには、開始、一時停止・再開、次へ、±1 分、リセット、ミュート、各設定の ± ボタンを置きます。補助表示板では操作系を省略できます。文字列の更新は 0.1 秒ごとに間引き、色・進捗・点滅は毎フレーム更新します。
次へボタンは 2 段階確認です。1 回目で 4 秒間だけ操作待ちになり、ラベルを「次へ」から「本当に？」へ変えます。2 回目でスキップし、4 秒を過ぎると何もせずに待ちを解除します。
操作パネルの `RotationAlertDisplay` は `uiRaycaster` と `interactRange` を持ちます。ローカルプレイヤーと Canvas の距離を 0.25 秒ごとに測り、`interactRange` 以下で有効化し、`interactRange + 0.5` を超えると無効化します。ヒステリシスは境界での点滅を防ぐためです。ローカルプレイヤーが null のエディター再生ではレイキャスターを有効のままにします。補助表示板では `uiRaycaster` は null です。

## 6. 音声

`RotationAlertAudio` は `RotationAlertCore` の時間と revision を監視します。初回観測時やスケジュール変更時には、直近 2 秒のキューだけを発火対象にします。途中参加者が過去の終了音を聞くことはありません。

音声はワールド全体で同じように聞こえるよう、installer が次の設定を行います。

```text
AudioSource.spatialBlend = 0
AudioSource.playOnAwake = false
AudioSource.volume = 1.0
VRCSpatialAudioSource.EnableSpatialization = false
```

ミュート状態はローカルだけに保持します。UI のクリック音も `RotationAlertAudio` から再生します。

### 音声キュー配列

`RotationAlertAudio` の `cueClips` / `cueVolumes` / `cueRepeats` は id 0〜4 に合わせた 5 要素です。id 3（Countdown）だけは null クリップで無音を維持します。id 4（AllFinished）は id 2 と同じ `rotation_end.wav` を使います。

## 7. Prefab と installer

installer は次のメニューを提供します。

- `Tools/TenteEEEE/Rotation Alert/Build Prefabs`
- `Tools/TenteEEEE/Rotation Alert/Install Panel into Current Scene`
- `Tools/TenteEEEE/Rotation Alert/Export UnityPackage`

`Build Prefabs` は、UdonSharp の program asset、フォント、UI 階層、Prefab を順に用意します。何度実行しても同じ場所に上書きできるようにします。`Install Panel into Current Scene` はパネルを現在のシーンに配置します。`Export UnityPackage` は `Assets/RotationAlert` 全体を書き出します。

日本語フォントは同梱の Noto Sans JP から TMP フォントアセットを生成します。program asset とフォントがまだ無い場合は installer が生成します。TMP Essential Resources が無い場合は、必要な導入手順をエラーとして表示します。

サブ表示板の `RotationAlertDisplay.core` は空のまま出荷します。ワールドに置いたあと、同じシーンの `RotationAlertCore` を参照させてください。

### 描画状態

installer は `Generated/RotationAlert UI Material.mat` と `Generated/RotationAlert TMP Material.mat` を生成し、再実行時にも同じパスを再利用します。どちらも render queue 3000 と `unity_GUIZTestMode = CompareFunction.LessEqual` を明示します。Canvas の sorting order は 0 です。これは `unity_GUIZTestMode` の継承値や sorting order に依存せず、通常の半透明ジオメトリと同じ奥行きテストで描くためです。BoxCollider は描画ではなく VRCUiShape の入力判定用です。

## 8. ファイル構成

```text
Assets/RotationAlert/
  README.md
  docs/SPEC.md
  Runtime/RotationAlertCore.cs
  Runtime/RotationAlertDisplay.cs
  Runtime/RotationAlertAudio.cs
  Runtime/RotationAlertShaderBridge.cs
  Runtime/*.asset
  Editor/RotationAlertInstaller.cs
  Editor/RotationAlertInstaller.Ui.cs
  Audio/rotation_start.wav
  Audio/warning.wav
  Audio/rotation_end.wav
  Audio/click.wav
  Fonts/NotoSansJP-Regular.otf
  Fonts/OFL-1.1.txt
  Fonts/RotationAlert JP SDF.asset
  Generated/rotation_alert_fill.png
  Generated/RotationAlert UI Material.mat
  Generated/RotationAlert TMP Material.mat
  Prefabs/RotationAlert Panel.prefab
  Prefabs/RotationAlert Display.prefab
```

ランタイムから外部アセットを参照しないことを原則にします。シェーダー連携を使う場合は、`RotationAlertShaderBridge` が `_UdonRotAlertState` と `_UdonRotAlertFlags` を更新します。値の意味は README に記載します。

## 9. 実装上の制約

- 時計は `Networking.GetServerTimeInSeconds()` を使う。`Time.time` は UI の点滅やリセット確認など、ローカルだけで完結する処理に限る。
- 所有者以外は同期フィールドを書き換えない。
- 音声に `SendCustomNetworkEvent` を使わない。
- UdonSharp の制約を避けるため、スケジュールを独自のクラスや構造体の配列で表現しない。
- double を整数化するときは `System.Math.Floor` を使う。`Mathf.FloorToInt` に double を渡さない。
- 時間表示は複合書式に頼らず、`ToString("00")` などの単純な連結で組み立てる。

Prefab の見た目、音の聞こえ方、複数人での同期は、Unity と VRChat SDK を開いて実際に動かして確認します。コードだけで確認できない部分を、動作確認済みとは扱いません。
