# 尖塔 PvP · 双人构筑

《杀戮尖塔 2》的双人 PvP Mod。当前源码版本：**0.2.31**。

在「多人模式 → 创建」选择 PvP，完成构筑后进入决斗。双方同时恢复能量、抽牌并出牌，每位玩家在自己的画面中位于左侧。

## 玩法与配置

- 10 / 15 / 20 次选牌、现开套牌、自定义白／蓝／金卡构筑；自定义按三张一组生成候选，组内不重复，组间可以重复。
- 手速对拼，或累计伤害、回合末统一结算。累计模式使用攻击意图显示预计伤害；多数非伤害效果立即生效。
- 可选 12 层战前发育，支持联机或独立发育。事件白名单见 [事件表](docs/pvp-event-whitelist-review.md)。
- 模组配置可调生命、格挡、召唤、中毒、灾厄倍率，以及壁垒、出牌上限、随机角色和相同种子。
- 相同种子对齐构筑与事件随机流；角色、牌池、牌组和决策相同的情况下，抽牌可保持一致。其他 Mod 自定义的随机机制需要单独适配。
- 提供通用角色 Mod 兼容，以及观者愤怒等特殊效果适配。特殊卡牌、遗物和动画仍可能需要针对性适配。

## 安装

双方需安装相同版本的游戏、BaseLib 和本 Mod；建议其他影响玩法的 Mod 也保持一致。

[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3813193750)

手动安装时，将编译生成的 `dist/SpirePvp` 文件夹放入游戏的 `mods` 目录。源码目录本身不能直接作为游戏 Mod 使用。

## LAN IP 联机

无需额外安装 `SlayTheSpire2.LAN.Multiplayer`。原有 Steam 建房入口保持不变。

1. 房主进入「多人模式 → PvP · LAN 建房」，查看本机 IPv4，填写 UDP 端口并创建房间。默认端口为 `33771`。
2. 另一位玩家进入「多人模式 → PvP · LAN 加入」，输入房主的 `IPv4:端口`，例如 `192.168.1.2:33771`。省略端口时使用 `33771`。
3. 双方在原有 PvP 房间中选择角色和构筑规则，准备后开始游戏。房间仍限两人；构筑、联机／独立发育和决斗共用原有同步流程。

支持同一局域网或可互通的虚拟局域网 IPv4；不提供公网打洞、自动发现、断线重连或 LAN 存档续玩。建房会监听所有网络接口，请只在可信网络使用。房主需允许游戏通过防火墙并放行所选 UDP 端口，不建议为此关闭整个防火墙。多网卡机器应选择与对方互通的 IPv4，不要填写 `0.0.0.0`、路由器地址或对方的 IP。

会记住建房端口和上次加入地址。连接使用游戏原生 ENet 与版本检查，双方仍需相同游戏、BaseLib 和 Mod 版本；其他网络 Mod 可能替换 ENet 协议，建议先仅启用 BaseLib 与 SpirePvp 排查。

## 从源码编译

需要 Windows、.NET 9 SDK、合法安装的《杀戮尖塔 2》及 BaseLib。当前代码针对游戏 v0.111.0 开发，游戏更新可能改变内部接口。

1. 将 `local.props.example` 复制为 `local.props`，填写自己的游戏和 BaseLib 路径。
2. 在 PowerShell 中执行以下命令。

```powershell
dotnet build ./SpirePvp/SpirePvp.csproj -c Release
dotnet run --project ./tests/SpirePvp.Tests.csproj -c Release -- "你的游戏安装目录"
./build.ps1 -GameDir "你的游戏安装目录"
```

`build.ps1` 先执行检查，再生成 `dist/SpirePvp-0.2.31.zip`。如需手动安装，退出游戏后执行 `./install.ps1 -GameDir "你的游戏安装目录"`。

观者集成检查是可选的；设置环境变量后可启用：

```powershell
$env:STS2_WATCHER_DLL = "你的观者 Mod 目录/Watcher.dll"
```

未设置时会明确显示 SKIP。自动检查不能代替两台电脑的实际联机测试。

仅检查 LAN 地址、端口与客户端 ID 时，不需要游戏或 BaseLib 程序集：

```powershell
dotnet run --project ./tests/SpirePvp.Tests.csproj -c Release -p:LanOnly=true
```

完整检查还会验证 LAN 设置兼容性及本机游戏的菜单、ENet 绑定补丁。发布前需双机验证：默认和自定义端口建房、无效地址、连接失败后重试、端口被占用、第三人加入被拒绝，以及完整构筑／发育／决斗流程；最后确认原有 Steam 建房未受影响。

## 项目结构

- `SpirePvp/`：Mod 源码、Harmony 补丁与配置。
- `tests/`：针对本机游戏程序集的离线检查和兼容性测试。
- `docs/`：发育事件白名单。
- `assets/`：自定义 PvP 地图图标与生成说明。

本仓库不分发游戏、BaseLib 或其他角色 Mod 的程序集，也不包含游戏反编译参考、用户日志和存档。相关依赖需自行安装。

## 许可与贡献

本项目原创源码采用 [MIT License](LICENSE)。自定义图标由 AI 工具生成，说明见 [图标文档](assets/map/pvp-duel-icon-v1.md)。游戏、BaseLib、其他 Mod 及其素材的权利归各自权利人所有，不受本项目许可证重新授权。

反馈问题时请附双方版本、启用的 Mod、模式与复现步骤；分享日志前请删去个人信息。欢迎通过 Issue 和 Pull Request 参与改进。
