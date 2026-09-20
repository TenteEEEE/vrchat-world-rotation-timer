# RotationAlert — 仕様（単一情報源）

VRChat ワールド向け「ローテーション制イベント用アラート」ギミック。prefab で配布する。
UdonSharp / VRChat Worlds SDK 3.8.x / Unity 2022.3 / TMP 3.0.6。

想定イベント: 交流会・展示回遊・グループトークなど、**会話中心でローテーションのある催し**。
音は「気づけるが会話を邪魔しない」短いチャイム。操作は誰でも可（権限制限なし）。

---

## 1. 用語とタイムライン

- **ローテ (Rotation)**: 1 区切りの活動時間。既定 15 分。
- **インターバル (Interval)**: ローテ間の休憩/移動時間。既定 2 分。
- **回数 (Count)**: ローテの回数。既定 6。
- **予告 (Warn)**: ローテ終了の N 分前の合図。既定 3 分。
- **開始前カウント (PreStart)**: 次ローテ開始の N 秒前の合図。既定 30 秒（Inspector のみ、在ワールドでは変更不可）。

タイムライン（Count=N）:

```
[R1][I][R2][I] ... [RN] → 完了
```

**最終ローテの後にインターバルは無い。** ローテ N 終了 = 全完了。

サイクル長 `cycle = rotationSec + intervalSec`。
スケジュール開始（ローテ 1 の開始）からの経過秒 `elapsed` に対して:

```
idx    = floor(elapsed / cycle)            // 0-based ローテ番号
within = elapsed - idx * cycle
if idx >= count                        → Finished
else if idx == count-1 && within >= rotationSec → Finished
else if within < rotationSec           → Rotation (idx+1), remaining = rotationSec - within
else                                   → Interval (after idx+1), remaining = cycle - within
```

`intervalSec == 0` も許容（ローテが連続する）。

## 2. キュー（合図）

| id | 名前 | 発火時刻（スケジュール開始からの秒） | 既定クリップ | 既定 repeat |
|---|---|---|---|---|
| 0 | RotationStart | `r*cycle` (r=0..N-1) | rotation_start.wav | 1 |
| 1 | Warning | `r*cycle + rotationSec - warnSec` (warnSec>0 かつ warnSec<rotationSec のとき) | warning.wav | 1 |
| 2 | RotationEnd | `r*cycle + rotationSec` (r=0..N-2) | rotation_end.wav | 1 |

`Countdown`（`r*cycle + rotationSec + intervalSec - preStartSec`）と `AllFinished`
（`(N-1)*cycle + rotationSec`）は外部連携用イベントとして通知するが、音声キューとしては扱わない。

- 各キューは **クリップ / 音量係数 / repeat 回数 (1..3) / repeat 間隔秒 (既定 0.6)** を Inspector で持つ。
- 音は **必ずローカルで鳴らす**（ネットワークイベントで鳴らさない）。全員が同じ絶対時刻から導出するので自然に揃う。
- **遅刻参加者・初回受信時はキューを遡って鳴らさない**（後述 §5）。

## 3. 同期モデル（RotationAlertCore）

`[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]`。**同期するのは絶対時刻とノブだけ。カウントダウン値は同期しない。**

```csharp
// --- 同期フィールド ---
[UdonSynced] public bool running;          // Start 後 true。Reset で false
[UdonSynced] public double scheduleStart;  // ローテ1 開始のサーバー時刻 (Networking.GetServerTimeInSeconds)
[UdonSynced] public double pausedAt;       // 0 = 非ポーズ。>0 ならこのサーバー時刻で凍結
[UdonSynced] public int rotationSec;       // 60..3600, 既定 900
[UdonSynced] public int intervalSec;       // 0..1800, 既定 120
[UdonSynced] public int rotationCount;     // 1..30, 既定 6
[UdonSynced] public int warnSec;           // 0..rotationSec-60, 既定 180 (0 = 予告なし)
[UdonSynced] public int revision;          // 操作のたびに +1。Display/Audio が変化検知に使う

// --- Inspector (非同期) ---
public int defaultRotationMinutes = 15;
public int defaultIntervalMinutes = 2;
public int defaultRotationCount = 6;
public int defaultWarnMinutes = 3;
public int preStartSec = 30;
public UdonBehaviour[] listeners;          // 外部連携先（任意）
```

Start 時点で `rotationSec` 等は既に同期値（在ワールド設定で変更可能）。`Start()` (Unity) で Inspector 既定値を同期フィールドへ入れる（所有者だけが意味を持つが全員入れてよい。遅刻者は直後に OnDeserialization で上書きされる）。

### 3.1 導出 API（毎フレーム呼んでよい。計算のみ、状態を書かない）

```csharp
public double EffectiveElapsed()   // running ? ((pausedAt > 0 ? pausedAt : now) - scheduleStart) : 0
public int    GetPhase()           // 0 Idle, 1 Rotation, 2 Interval, 3 Finished  （Paused は IsPaused() で別軸）
public bool   IsPaused()           // running && pausedAt > 0
public int    GetRotationIndex()   // 1-based。Rotation 中は現在、Interval 中は「終わったローテ」の番号。Idle=0, Finished=rotationCount
public float  GetRemaining()       // 現フェーズの残り秒。Idle=rotationSec, Finished=0
public float  GetPhaseLength()     // 現フェーズの全長秒（プログレスバー用）
public int    Cycle()              // rotationSec + intervalSec
```

### 3.2 操作（すべて public、UI ボタンから SendCustomEvent で呼ぶ）

すべての操作は次の順で行う: `TakeOwnership()` → 状態変更 → `revision++` → `RequestSerialization()`。
所有者自身には OnDeserialization が来ないが、導出モデルなので追加処理は不要。

| イベント名 | 条件 | 動作 |
|---|---|---|
| `OpStart` | !running または Finished | `scheduleStart = now; pausedAt = 0; running = true` |
| `OpPauseResume` | running && !Finished | 非ポーズなら `pausedAt = now`。ポーズ中なら `scheduleStart += now - pausedAt; pausedAt = 0` |
| `OpSkip` | running && !Finished | 現フェーズの残りを 0 にする: `scheduleStart -= remaining`（ポーズ中も可、pausedAt 基準で同じ式） |
| `OpPlusMinute` | running && !Finished | 現フェーズ残りを +60 秒。ただし `remaining+60 > phaseLength` なら残り = phaseLength に丸める（前フェーズへ逆戻りしない） |
| `OpMinusMinute` | running && !Finished | 現フェーズ残りを −60 秒。`remaining <= 60` なら Skip と同じ（残り 0） |
| `OpResetRequest` | 常時 | 2 段階確認。1 回目: `resetArmedUntil = Time.time + 4` を立てるだけ（ローカル、非同期）。2 回目（4 秒以内）: `running = false; pausedAt = 0` |
| `OpRotationPlus` / `OpRotationMinus` | !running | rotationSec ±60（60..3600）。warnSec が rotationSec−60 を超えたら詰める |
| `OpIntervalPlus` / `OpIntervalMinus` | !running | intervalSec ±60（0..1800） |
| `OpCountPlus` / `OpCountMinus` | !running | rotationCount ±1（1..30） |
| `OpWarnPlus` / `OpWarnMinus` | !running | warnSec ±60（0..rotationSec−60） |

「残り秒を変える」操作は共通ヘルパー `ShiftRemaining(double delta)` で実装する:
`scheduleStart -= delta` ではなく、**「新しい残り秒 targetRemaining を決めてから `scheduleStart = anchor - (elapsedAtPhaseStart + (phaseLength - targetRemaining))`」** の形で書く（anchor = pausedAt>0 ? pausedAt : now）。浮動小数の累積を避け、Skip が境界ぴったりに着地する。

`running == false` のときの設定ボタンだけ有効。running 中は設定ボタンを `interactable=false` にする（Display 側で毎フレーム反映）。

### 3.3 外部連携（listeners）

キュー発火時（Audio と同じタイミング、各クライアントでローカルに）、`listeners` の各 UdonBehaviour に `SendCustomEvent` する:
`OnRotationStart` / `OnRotationWarning` / `OnRotationEnd` / `OnIntervalCountdown` / `OnAllFinished`。
さらに revision 変化時に `OnScheduleChanged`。null 要素はスキップ。
（アバターギミックへ直接は繋げない。世界側の照明/演出用。音経由の AudioLink 連携は別途ユーザー側で可。）

## 4. 表示（RotationAlertDisplay）

ローカル専用。`public RotationAlertCore core;` を参照し、毎フレーム導出 API を読む。同期なし。何枚でも置ける。

表示要素（全て TMP、Inspector で参照）:

| フィールド | 内容 |
|---|---|
| `phaseText` | `待機中` / `ローテーション 3 / 6` / `インターバル（次: 4 / 6）` / `一時停止中` / `全ローテ終了` |
| `timeText` | `mm:ss`（残り秒を切り上げ: `ceil`。Idle はローテ長、Finished は `00:00`） |
| `subText` | 次の予定。Rotation: `終了まで` + 予告があれば `予告 3分前`。Interval: `次のローテ開始まで`。Idle: `15分 × 6回 ／ 休憩 2分 ／ 予告 3分前`。Finished: `おつかれさまでした` |
| `progressFill` (Image, Filled Horizontal) | `1 - remaining/phaseLength` |
| `accentImages` (Image[]) | フェーズ色を塗る帯・枠 |

フェーズ色:
- Idle: グレー `#6B7A85`
- Rotation: ティール `#2ED3C6`
- Rotation かつ `remaining <= warnSec` (warnSec>0): アンバー `#F4B942`
- Rotation かつ `remaining <= 60`: 赤 `#E05A4E`
- Interval: ブルー `#4C8DFF`
- Paused: グレー、`timeText` を 0.5 秒周期で点滅（alpha 1↔0.35）
- Finished: グリーン `#3CC46B`

操作パネル用の追加参照（サブ表示板では null 可、null はスキップ）:
- `pauseButtonLabel` (TMP): `一時停止` / `再開`
- `startButton` (Button): `!running || Finished` のとき interactable
- `runControlButtons` (Button[]): 一時停止/次へ/+1分/−1分。`running && !Finished` のとき interactable
- `settingButtons` (Button[]): 設定 ±。`!running` のとき interactable
- `settingsText` (TMP): `ローテ 15分 ／ 休憩 2分 ／ 回数 6 ／ 予告 3分前`
- `resetButtonLabel` (TMP): 通常 `リセット`、armed 中 `もう一度押すと初期化`
- `muteButtonLabel` (TMP): `音: ON` / `音: OFF`（RotationAlertAudio.muted を反映）

更新頻度: 文字列生成は 0.1 秒間隔（`Time.time` で間引き）。色/点滅/fill は毎フレームでよい。

## 5. 音（RotationAlertAudio）

ローカル専用。`public RotationAlertCore core; public AudioSource source;`。

```csharp
public AudioClip[] cueClips = new AudioClip[3];   // §2 の id 順
public float[] cueVolumes = {1,1,1};
public int[] cueRepeats = {1,1,1};
public float repeatGap = 0.6f;
public bool muted;                                 // ローカル。ToggleMute() で反転
public AudioClip uiClick; public float uiClickVolume = 0.35f;  // ボタン押下音（PlayUiClick()）
```

判定ロジック（毎フレーム）:

```
if (!core.running) { armed = false; lastRevision = core.revision; return; }
elapsed = core.EffectiveElapsed()
if (!armed || core.revision != lastRevision) {
    // 初回観測（遅刻参加・Start 押下・スケジュール変更）はすべて同じ扱い:
    // 直近 grace=2.0 秒以内のキューだけ拾い、それより古いものは「鳴らさずに通過済み」にする。
    // → 14 分前の終了音が遅刻者に鳴ることはなく、Start/Skip 直後の境界キューは遅延があっても漏れない。
    lastElapsed = elapsed - 2.0
    armed = true; lastRevision = core.revision
    (listeners に OnScheduleChanged)
}
// (lastElapsed, elapsed] に入る全キューを発火（複数入ることがある。順序は時刻順）
lastElapsed = elapsed
```

比較は `t > lastElapsed && t <= elapsed + 1e-3`。
キュー時刻の列挙は §2 の式をローテ番号 r について走査する（idx 近傍 r-1..r+1 だけ見れば十分。ただし grace 窓や Skip でまたぐことがあるので `[lastElapsed, elapsed]` に交差する r の範囲を計算して回す）。

repeat: `source.PlayOneShot(clip, vol)` を回数分、`SendCustomEventDelayedSeconds("PlayPendingRepeat", gap)` で刻む。
同時に複数イベントが発火したら、listeners には全部送る。音声はそのフレーム内の音声キュー（id 0..2）のうち
id が大きいものだけ鳴らし、Countdown / AllFinished は音声再生を抑止する。

AudioSource 設定（installer が設定）: `spatialBlend = 0`、`playOnAwake = false`、`volume = 0.6`。
同じ GameObject に `VRC.SDK3.Components.VRCSpatialAudioSource` を付け `EnableSpatialization = false`（VRChat が実行時に空間化を強制するのを止め、会場全体で同じ音量にする）。

## 6. UI レイアウト（installer が生成する操作パネル）

World-space Canvas 720×460 (scale 0.001、`VRCUiShape` + BoxCollider trigger)。フォントは Noto Sans JP（§7）。

```
┌──────────────────────────────────────────────┐
│  [accent 帯 8px]                                │
│  ローテーション 3 / 6                    音: ON │  ← phaseText(28pt 左) / mute(小ボタン右上)
│                                                  │
│                 1 2 : 3 4                        │  ← timeText 120pt 中央
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░               │  ← progress 16px
│  終了まで ／ 予告 3分前                          │  ← subText 20pt
│──────────────────────────────────────────────────│
│ [ 開始 ] [一時停止] [ 次へ ] [ +1分 ] [ −1分 ]  [リセット] │  ← 各 100×56、リセットは赤系
│──────────────────────────────────────────────────│
│ 設定（待機中のみ）  ローテ 15分 ／ 休憩 2分 ／ 回数 6 ／ 予告 3分前 │
│ ローテ [−][+]   休憩 [−][+]   回数 [−][+]   予告 [−][+]         │  ← 各 44×44
└──────────────────────────────────────────────┘
```

配色（PocketConveni と同系のダーク）: 背景 `#07131B`、面 `#0F2431`、文字 `#EAF4F5`、控えめ文字 `#8FA8B3`、主操作 `#1D8A93`、危険 `#E05A4E`。
全ボタンは `onClick` に **2 つ**の persistent listener: `core.SendCustomEvent("Op…")` と `audio.SendCustomEvent("PlayUiClick")`（ミュート/リセットも同様）。ミュートは `audio.SendCustomEvent("ToggleMute")`。

**サブ表示板** prefab（`RotationAlert Display.prefab`）: Canvas 720×300、phaseText / timeText / progress / subText のみ。Display コンポーネントの `core` は空（設置後にユーザーがドラッグ）。Audio は持たない。

## 7. ファイル構成（すべて `Assets/RotationAlert/` 配下で完結。外部参照禁止）

```
Assets/RotationAlert/
  README.md                         利用者向け（日本語）
  docs/SPEC.md                      本書
  Runtime/RotationAlertCore.cs      namespace RotationAlert
  Runtime/RotationAlertDisplay.cs
  Runtime/RotationAlertAudio.cs
  Runtime/RotationAlertShaderBridge.cs  _UdonRotAlertState/_UdonRotAlertFlags を毎フレーム VRCShader.SetGlobalVector（README の表が契約）
  Runtime/*.asset                   UdonSharpProgramAsset（installer が生成）
  Editor/RotationAlertInstaller.cs  MenuItem "Tools/Rotation Alert/..."
  Editor/RotationAlertInstaller.Ui.cs  UI 生成ヘルパー（PocketConveniInstaller.Primitives.cs を参考に自前で書く。参照はしない）
  Audio/rotation_start.wav, warning.wav, rotation_end.wav, click.wav
  Fonts/NotoSansJP-Regular.otf, OFL-1.1.txt, RotationAlert JP SDF.asset (installer が生成)
  Prefabs/RotationAlert Panel.prefab, RotationAlert Display.prefab (installer が生成)
```

installer メニュー:
- `Tools/Rotation Alert/Build Prefabs` — program asset 確認→コンパイル→フォント生成→階層構築→`PrefabUtility.SaveAsPrefabAsset`→一時オブジェクト削除。冪等（既存 prefab は上書き）。
- `Tools/Rotation Alert/Install Panel into Current Scene` — 上を実行してから prefab をシーンにインスタンス化（位置 (0, 1.3, 0)）。
- `Tools/Rotation Alert/Export UnityPackage` — `Assets/RotationAlert` を `RotationAlert.unitypackage`（プロジェクト直下）に `AssetDatabase.ExportPackage(..., ExportPackageOptions.Recurse)`。

フォント: PocketOverdriveInstaller.Assets.cs `EnsureJapaneseFontAsset` と同じ方式で `TMP_FontAsset.CreateFontAsset(otf)` を Dynamic で生成し、UI で使う全文字（installer 内の定数文字列 + ASCII 印字可能文字）を `TryAddCharacters` で事前に載せる。TMP Essential Resources 未導入なら明示エラー。

program asset: PocketConveniInstaller.cs `EnsureProgramAsset` と同じ手順（source 紐づけ→`UdonSharpCompilerV1.CompileSync`→初回生成時は「もう一度実行」を例外で促す）。
prefab 保存前に各 proxy へ `UdonSharpEditorUtility.CopyProxyToUdon`。

## 8. 落とし穴（実装契約）

1. **遅刻参加者に遡り発火しない**（§5 grace 窓 2 秒。それより古いキューは鳴らさない）。
2. **音はローカル**。SendCustomNetworkEvent 禁止。
3. **VRCSpatialAudioSource.EnableSpatialization=false** を付ける。
4. **所有権**: 操作は `Networking.SetOwner(Networking.LocalPlayer, gameObject)` → 変更 → `RequestSerialization()`。所有者以外は同期フィールドを書かない。
5. `Networking.GetServerTimeInSeconds()`（double）を唯一の時計にする。`Time.time` はローカル UI 用（リセット確認・点滅）に限る。
6. UdonSharp 制約: `double` の同期は可。`Mathf.FloorToInt` は float 引数なので double は `(int)System.Math.Floor(x)` を使う。string.Format の複合書式は避け、`ToString("00")` 連結で mm:ss を作る。
7. スケジュール構造体/クラスは作らない（UdonSharp は独自クラスの配列に制約がある）。全てプリミティブと配列。
8. 生成した Unity 状態（prefab, 音の聞こえ方, 同期）は **Unity を開いて確認するまで未検証**。報告にそう書く。
