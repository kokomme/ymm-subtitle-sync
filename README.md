# YmmSubtitleSync — 字幕タイミング調整プラグイン

[![Release](https://img.shields.io/github/v/release/kokomme/ymm-subtitle-sync?label=ダウンロード&style=for-the-badge)](https://github.com/kokomme/ymm-subtitle-sync/releases/latest)

[ゆっくりMovieMaker4 (YMM4)](https://manjubox.net/ymm4/) 向けプラグインです。  
**ボイスアイテムが話している間だけ字幕を表示**し、話し終わったら字幕を消します。

---

## 解決する問題

### 状況

表情差分を話し終わった後も維持するために、ボイスアイテムを音声より長く伸ばしている場合:

```
Before:
  A: [音声(2s)▶▶][表情維持(1s)  ]  ← 字幕が3s間表示される
  B:                 [音声(2s)▶▶]  ← 字幕が重なる！

After（このプラグイン適用後）:
  A: [音声(2s)▶▶][表情維持(1s)  ]  ← アイテム長はそのまま
     ↑字幕は音声(2s)だけ             表情差分は継続される
  B:                 [音声(2s)▶▶]  ← 字幕が重ならない！
```

---

## プラグインパネルの使い方

YMM4 にインストール後、サイドパネルに **「字幕タイミング調整」** が表示されます。

```
┌─────────────────────────────┐
│ 字幕タイミング調整           │
│                              │
│ 字幕グループ                 │
│  ボイスが終了したら字幕を消す│
│  ☑ すべて選択               │
│  ─────────────────────────  │
│  ☑ ずんだもん               │
│  ☑ 春日部つむぎ             │
│  □ 四国めたん               │
│                              │
│  [キャラ一覧を更新]          │
│                              │
│  [適用する]                  │
└─────────────────────────────┘
```

### 手順

1. YMM4 でプロジェクトを開き、**保存する**
2. プラグインパネルの **「キャラ一覧を更新」** を押す
3. 字幕を音声に合わせたいキャラにチェックを入れる
4. **「適用する」** を押す
5. YMM4 でプロジェクトを**再読み込み**する（自動再読み込みされる場合あり）

---

## 処理内容

1. **ボイスアイテムの内蔵字幕を無効化**  
   `IsSubtitleEnabled` 等のフィールドを `false` に設定

2. **音声実尺と同じ長さのテキストアイテムを追加**  
   「字幕（自動生成）」タイムラインに字幕専用アイテムを追加  
   → 表示時間 = `VoiceLength + AdditionalTime` のフレーム数のみ

---

## インストール

### 必要環境

- ゆっくりMovieMaker4 v4.23.0.0 以降
- Windows 10 version 2004 (ビルド 19041) 以降
- .NET 8.0 以降のランタイム（YMM4 に同梱）

### ダウンロード＆インストール

1. **[最新リリースのページ](https://github.com/kokomme/ymm-subtitle-sync/releases/latest)** を開く
2. `YmmSubtitleSync.ymme` をダウンロード
3. `.ymme` ファイルを **YMM4 のウィンドウにドラッグ＆ドロップ**
4. YMM4 を再起動

### ビルド手順（開発者向け）

```bash
# YMM4 の DLL を Libs/ にコピー
copy "C:\ゆっくりMovieMaker4\YukkuriMovieMaker.Plugin.dll" Libs\

# ビルド
dotnet build -c Release -r win-x64 --no-self-contained
```

`v` から始まるタグを push すると GitHub Actions が自動でビルド＆リリースを作成します。

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## 注意事項

- 適用前に `.ymmp.bak` バックアップが自動作成されます
- YMM4 を閉じた状態では動作しません（プロジェクトパス検出のため）
- **初回使用時**: YMM4 の .ymmp 内の字幕制御フィールド名（`IsSubtitleEnabled` 等）が  
  YMM4 のバージョンによって異なる場合、適用後の動作確認が必要です。  
  フィールドが見つからない場合はテキストアイテム追加のみ行われます。

## ライセンス

MIT
