# U.N. VRC PerfectSync

VRChat アバターの既存 BlendShape から、Perfect Sync 用の52個の BlendShape を確認・作成する Unity Editor ツールです。

VRCFT、OSC、Animator Controller の設定は行いません。

[公式サイト](https://usagi.github.io/un-vrc-perfectsync/)では、画面例を見ながら機能を確認し、そのまま VCC へ追加できます。

## インストール

### VCC Package Manager（推奨）

[公式サイトの Add to VCC](https://usagi.github.io/un-vrc-perfectsync/#install) を開き、VCC に repository を追加します。

リンクで開けない場合は、VCC の `Settings > Packages > Add Repository` に次の URL を追加してください。

```text
https://usagi.github.io/un-vrc-perfectsync/vcc/index.json
```

追加後、VCC で対象プロジェクトの `Manage Project` を開き、Package Manager から `U.N. VRC PerfectSync` をインストールします。

### Unity Package Manager

1. Unity Editor で `Window > Package Manager` を開きます。
2. 左上の `+` から `Add package from git URL...` を選びます。
3. 次の URL を入力して `Add` を押します。

```text
https://github.com/usagi/un-vrc-perfectsync.git?path=/unity/un-vrc-perfectsync
```

## 使い始める

1. Unity で `Tools > U.N. VRC > PerfectSync Authoring` を開きます。
2. `Face Object` に顔の `SkinnedMeshRenderer` を持つオブジェクトを指定します。
3. `Auto Setup` の候補を確認するか、Todo の Key を選んで元になる BlendShape を割り当てます。

詳しい操作、状態の意味、バックアップと再設定は [Manual](docs/manual.md) を参照してください。

## 動作環境

- Unity 2022.3
- VRChat Avatars 3.0 プロジェクト

## License

[MIT License](LICENSE)
