---
name: git-push-ssl
description: 本机 git 推送 GitHub 需 -c http.sslVerify=false（证书链缺失）
metadata: 
  node_type: memory
  type: project
  originSessionId: 84d7e7cc-b226-48d1-935d-a3e22b84ee0a
---

# git 推送环境（2026-08-04 确认）

本机 git 对 `https://github.com/XKLMY-hi/OpenUTAU-Plus.git` 推送报
`SSL certificate OpenSSL verify result: unable to get local issuer certificate`。

**How to apply:** 推送时用
`git -c http.sslVerify=false push origin plus-develop`
（SSL 校验仅本次跳过，不写入全局配置）。

# 凭据助手弹窗 / store 匹配失败（2026-09-21 实测）

现象：push 弹出 `CredentialHelperSelector`（manager/wincred/no helper 三选一），既不弹浏览器也无法用硬盘上已存的凭据。

根因与处置（两层，都已修）：

1. **`<盘符>:\home\.git-credentials` 行尾是 CRLF**：`git-credential-store` 把 `\r` 并进主机名 → 明明有 `github.com` 条目也匹配不上 → git 只好回退调 GCM 的 `helper-selector` 弹窗。修法：`sed -i 's/\r$//' <盘符>:/home/.git-credentials`（修完 70→69 字节）。
2. **系统级 gitconfig 设了 `credential.helper = helper-selector`**（`<盘符>:\tools\PortableGit\etc\gitconfig`），排在用户 store 之前。已在 `<盘符>:\home\.gitconfig` 里重置列表：`[credential]` 下先 `helper =`（空值清空）再 `helper = store`，只走硬盘凭据。

诊断命令（输出自动打码，不泄露 token）：

```bash
printf 'protocol=https\nhost=github.com\n\n' | git credential-store get | sed -E 's/^password=.*/password=***/'
```

打印存储文件结构（打码）确认行尾/格式：`sed -E 's|(:)[^@]*@|\1MASK@|' ~/.git-credentials | cat -A`（`^M$` 即 CRLF 问题）。
