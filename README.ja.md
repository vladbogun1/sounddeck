<div align="center">

<img src="docs/logo.png" width="120" alt="SoundDeck ロゴ" />

# SoundDeck

**どのアプリの音でも、どのデバイスへも — かんばんボードのように。**
アプリごとの音声ルーティング、音量、出力ミラー、レイアウト保存ができる、モダンな Windows 常駐アプリ。

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)
![Themes](https://img.shields.io/badge/themes-20-ff4fd8)

[English](README.md) · [中文](README.zh.md) · [Español](README.es.md) · [Русский](README.ru.md) · [Deutsch](README.de.md) · **日本語**

<img src="docs/theme-neu.png" width="720" alt="SoundDeck の画面" />

</div>

---

## 概要

SoundDeck はシステムトレイに常駐し、Windows の音声を**かんばんボード**に変えます。列は出力デバイス、カードは実行中のアプリ。カードをデバイスにドラッグすれば、そのアプリの音をそこへ送れます — もう Windows のサウンド設定を掘り返す必要はありません。

## 機能

- 🎛️ **ドラッグ＆ドロップでルーティング** — アプリのカードをドラッグして任意の出力デバイスへ。
- 🔊 **アプリ／デバイスごとの音量・ミュート** — カードをクリックすると、ドラッグできるつまみ付きのスライダーが開きます。
- ⭐ **ワンクリックで既定デバイス**。
- 🎧 **2 つのデバイスへ同時出力** — メイン出力を 2 台目にミラー（例：テレビ**と**ヘッドホン）。
- 🩹 **自動修復** — デバイスがスリープから復帰し Windows が既定を失った（あの空のドロップダウン）とき、レイアウトを自動で再適用します。
- 💾 **プロファイル** — レイアウトごと保存し、コンパクトなメニューから切り替え。
- 🚀 **ログイン時に起動**（任意）。
- 🎨 **20 テーマ** — カスタムのテーマ選択から（下記）。
- 🌍 **多言語** — 既定はシステム言語、設定で変更可。
- 🧩 **スマートまとめ** — 複数プロセスのアプリ（Spotify、Chrome、Discord）を 1 枚のカードにまとめて一緒にルーティング（設定でオフ可）。
- 🖱️ **トレイでトグル** — クリックで開き、もう一度で隠す。

## テーマ

丁寧に調整した 20 テーマを、カスタムのドロップダウンで切り替え：

<div align="center">
<img src="docs/themes.gif" width="720" alt="SoundDeck のテーマ" />
</div>

<table>
<tr>
<td><img src="docs/theme-win11.png" width="260" alt="Windows 11" /><br/><b>Windows 11 (Fluent)</b></td>
<td><img src="docs/theme-ios.png" width="260" alt="iOS glass" /><br/><b>iOS glass</b></td>
<td><img src="docs/theme-dracula.png" width="260" alt="Dracula" /><br/><b>Dracula</b></td>
</tr>
<tr>
<td><img src="docs/theme-synthwave.png" width="260" alt="Synthwave" /><br/><b>Synthwave</b></td>
<td><img src="docs/theme-nord.png" width="260" alt="Nord" /><br/><b>Nord</b></td>
<td><img src="docs/theme-matrix.png" width="260" alt="Matrix" /><br/><b>Matrix</b></td>
</tr>
</table>

その他：Neubrutalism（既定）、Dark glass、Material、Paper、Solarized、Gruvbox、Monokai、Catppuccin、Rosé Pine、OLED、Forest、Ocean、Sunset、Cyberpunk。

## インストール

1. [最新リリース](../../releases/latest) から **`SoundDeck.exe`** をダウンロード。単一のポータブルファイル（self-contained .NET 8）で、インストール不要・.NET も不要です。
2. **Microsoft Edge WebView2 ランタイム**があることを確認（最近の Windows 10/11 にはプリインストール）。
3. 実行すると SoundDeck がトレイ（スピーカーアイコン）に常駐します。アイコンを**左クリック**でボードを開きます。

> **SmartScreen について。** リリースの exe は**自己署名**で、信頼された CA による署名ではないため、Windows SmartScreen が警告することがあります — **詳細情報 → 実行**を選んでください。発行元の確認・信頼は同梱の `SoundDeck-PublicCert.cer` をインポートして行えます。信頼された商用コード署名証明書を使えば警告は完全になくなります。

## 仕組み

- **シェル：** C# / .NET 8 の **WPF** ホスト。トレイアイコンと枠なしフライアウト。
- **UI：** **WebView2** でレンダリング — 素の HTML/CSS/JS（フレームワークなし）、`postMessage` で C# と通信。
- **オーディオ：** Core Audio は [NAudio](https://github.com/naudio/NAudio)。既定デバイスとアプリ別ルーティングは**非公開の** COM インターフェイス `IPolicyConfig` / `IAudioPolicyConfig` を使用（[EarTrumpet](https://github.com/File-New-Project/EarTrumpet) と同じ手法）。Windows 10 1803 以降と Windows 11 で動作。
- **ミラー：** メインデバイスの WASAPI ループバック取得 → 2 台目で `WasapiOut` 再生（その形式へリサンプル）。

## ソースからビルド

```bash
cd app
dotnet run                       # 開発ビルド
```

ポータブルな単一ファイルのリリース：

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## インストーラー（任意）

[`installer/SoundDeck.iss`](installer/SoundDeck.iss) に [Inno Setup](https://jrsoftware.org/isinfo.php) スクリプトがあります。Inno Setup 6（`ISCC.exe`）でビルドすると、スタートメニューのショートカットとアンインストーラー付きの `setup.exe` ができます。

## 制限事項

- **ミラーはデバイス単位** — 2 台目がメイン出力をミラーします。アプリ単位の本当のミラーにはプロセスループバックが必要（予定）。
- ルーティングは**非公開の** Windows COM インターフェイスを使用。Windows のビルドによって動作が異なる場合があります。

## クレジット

- アイコン：[Font Awesome Free](https://fontawesome.com)（CC BY 4.0）。
- 手法は [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) を参考にしています。

## ライセンス

[MIT](LICENSE) © 2026 vladbogun1
