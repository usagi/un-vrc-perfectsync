# Release

保守者向けの VCC package リリース手順です。

## 事前確認

- `unity/un-vrc-perfectsync/package.json` の version とリリース version が一致している
- `unity/un-vrc-perfectsync/CHANGELOG.md` に対象 version がある
- README、Manual、package 内 README が現行 UI と一致している
- 隔離 Unity project で Editor assembly のコンパイルが通る
- `git status --ignored --short` で source と生成物の境界を確認する

## 生成

repository root で実行します。

```powershell
pwsh ./scripts/Build-Release.ps1 -Version 1.0.0
```

出力:

- `release-packages/network.usagi.vrc-perfectsync-1.0.0.zip`
- `docs/vcc/index.json`

zip の root が package root になり、`package.json` は zip の直下へ入ります。zip は生成物のため Git へ追加しません。`docs/vcc/index.json` は VCC repository listing なので追跡します。

既定以外の配布先を使う場合:

```powershell
pwsh ./scripts/Build-Release.ps1 `
  -Version 1.0.0 `
  -BaseUrl "https://github.com/usagi/un-vrc-perfectsync/releases/download/1.0.0"
```

## 公開前検査

1. zip を展開し、直下の `package.json`、`Editor/`、`README.md`、`CHANGELOG.md`、`LICENSE.md` を確認する。
2. zip 内 `package.json` の version、url と `docs/vcc/index.json` の listing が一致することを確認する。
3. `zipSHA256` が生成した zip の SHA-256 と一致することを確認する。
4. tag／GitHub Release 名を `1.0.0` とし、zip を同じ Release へ添付する。
5. 更新した `docs/vcc/index.json` を公開し、VCC から repository と package を確認する。

hash と package URL は build script が生成します。手作業で書き換えません。

## GitHub Release description

公式 Web サイトと VCC repository listing は GitHub Pages から公開します。

- 公式サイト: `https://usagi.github.io/un-vrc-perfectsync/`
- VCC repository: `https://usagi.github.io/un-vrc-perfectsync/vcc/index.json`

`docs/` の更新は `.github/workflows/pages.yml` が Pages へ公開します。GitHub の repository settings で Pages の source を `GitHub Actions` に設定します。

各 version の GitHub Release description にも導入方法を記載し、公式サイトを経由しなくてもインストールできる状態を保ちます。

各 Release description に、少なくとも次の内容を記載します。

```markdown
## インストール

### VCC Package Manager（推奨）

[Add to VCC](vcc://vpm/addRepo?url=https%3A%2F%2Fusagi.github.io%2Fun-vrc-perfectsync%2Fvcc%2Findex.json) を開き、repository を登録します。

リンクで開けない場合は、VCC の `Settings > Packages > Add Repository` に次の URL を追加してください。

`https://usagi.github.io/un-vrc-perfectsync/vcc/index.json`

登録後、対象プロジェクトの `Manage Project` から `U.N. VRC PerfectSync` をインストールします。

Unity Package Manager から直接導入する方法と使い方は [README](https://github.com/usagi/un-vrc-perfectsync#readme) を参照してください。
```

Release description の先頭には対象 version の主な変更を簡潔に置き、その後に上記のインストール案内を置きます。
