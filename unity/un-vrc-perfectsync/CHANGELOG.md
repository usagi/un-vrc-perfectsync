# Changelog

## 1.0.1

- Auto Setup の合成・左右分割について、生成元と生成方法の保存・表示に対応。
- `Reset Key...` を追加。旧版で作った既存 Key も個別に解除でき、他の表情と Weight を保持。
- Rescan 後に古い変形キャッシュを使って合成する問題を修正。
- 割り当て済みでも、出力 Key の欠落・空形状・大文字小文字の不一致を検出。
- 元 Mesh の共有だけ、または階層名の一致だけで別の mapping profile を選ぶ問題を修正。

## 1.0.0

- Perfect Sync 52 Key の状態確認と手動割り当てに対応。
- 既存 BlendShape、左右、Avatar Descriptor、変形領域を使う保守的な Auto Setup に対応。
- 設定済み Key の自動再設定と、全 Key を UNDEFINED に戻す Reset に対応。
- Key ごとのプレビュー、検索、係数付き BlendShape 合成に対応。
- Case 修正、空 Key 作成、BlendShape Backup／Restore に対応。
- working mesh と renderer ごとの mapping profile によって元の Mesh を保護。
