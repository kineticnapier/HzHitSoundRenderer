# HzHitSoundRenderer

高KPS / Hz Charting 用のヒット音PCMレンダラーです。

## 基本方針

- ヒット音イベントは間引きません。
- 元の hitSound `AudioClip` のPCMをそのまま出力バッファへ重ねます。
- `ヒット音倍率` は0〜1の単純な線形倍率だけです。1.0が元音量です。
- `コピーする長さ 秒` は0が既定で、元音全体を使用します。

## 使い方

UMMで有効化し、再生方式を選んで通常どおり譜面を再生してください。

- `分割先読み`: 軽量で編集確認向け
- `全セグメント事前焼き`: 再生前に全セグメントを生成
- `全体1本焼き`: 境界ノイズを避けるため全体を1本のAudioClipへ生成

音が強すぎる場合は `ヒット音倍率` を下げてください。音量を1倍より上へ増幅する機能や音色加工はありません。

## 対応環境

- A Dance of Fire and Ice v3.3.1
- Unity Mod Manager
- .NET Framework 4.8をビルドできるWindows環境

## ビルド

`ADOFAI_GAME_MANAGED_DIR`へゲームの`Managed`フォルダーを指定してからReleaseビルドします。

```powershell
$env:ADOFAI_GAME_MANAGED_DIR = "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"
dotnet build -c Release
```
