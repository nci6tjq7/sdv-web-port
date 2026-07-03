# Stardew Valley Web Port — Design Document

**Status**: Self-reviewed v2, awaiting user review
**Date**: 2026-07-03
**Author**: Brainstorming session output
**Project root**: `/home/z/my-project/`

---

## 1. 项目目标与硬约束

### 1.1 目标

将 Stardew Valley（GOG DRM-free 版本）移植到浏览器可运行，并支持 SMAPI 模组与 XNB 资源编辑。

### 1.2 硬约束（不可妥协）

| 编号 | 约束 | 否则后果 |
|---|---|---|
| C1 | **浏览器可玩** | 项目失去意义 |
| C2 | **SMAPI 可用** | 项目失去差异化价值 |
| C3 | **用户 GOG 副本提供文件** | 法律姿态不成立 |
| C4 | **不反编译、不重写游戏代码** | 触发著作权侵权 |
| C5 | **不公开部署** | 触发 GOG EULA 分发禁止条款 |

### 1.3 非目标（明确不做）

- ❌ 多人联机（SDV 自带联机需 Galaxy SDK，浏览器无法支持）
- ❌ 移动端优化（桌面端验收通过后再考虑）
- ❌ 100% mod 兼容（追求 T1+T2 80%+，T3/T4 明确放弃）
- ❌ 性能对标 PC 版（接受 20-40 FPS 中-低性能，农场场景最低 20 FPS）
- ❌ 反编译 SDV 为源码再重写（法律禁区）

---

## 2. 法律姿态与边界

### 2.1 项目定性

**"互操作运行器"（Interoperative Runtime Emulator）**

- 类比 Ruffle（Flash 模拟器）、webretro（RetroArch 浏览器版）
- 运行器本身不含任何 Stardew Valley 代码或资源
- 用户必须自行提供合法 GOG 副本
- 不部署到公网，仅本地/内网使用

### 2.2 法律依据

- **EU Directive 2009/24/EC Art. 6**: 允许为实现互操作性进行逆向工程
- **US Sega v. Accolade (9th Cir. 1992)**: 互操作目的的反编译属合理使用
- **GOG EULA**: 授权个人使用，未禁止在同一设备上换运行时
- **SMAPI License**: MIT 开源，允许 fork 与修改

### 2.3 红线行为

| 行为 | 允许 | 禁止 |
|---|---|---|
| 在浏览器加载原始 GOG DLL | ✅ | |
| 在浏览器加载 SMAPI（MIT） | ✅ | |
| 解包 XNB 资源为 JSON 供编辑 | ✅ | |
| 重新打包用户修改的 XNB 注入游戏 | ✅ | |
| 反编译 DLL 为 C# 源码 | | ❌ |
| 移除 GOG Galaxy SDK 调用 | | ❌ |
| 公网公开部署让任何人访问 | | ❌ |
| 服务器托管游戏文件供下载 | | ❌ |
| 写绕过 SMAPI 反作弊的代码 | | ❌ |

---

## 3. 架构总览（5 层栈）

```
┌─────────────────────────────────────────────────────────┐
│ Layer 5: Browser UX                                     │
│   HTML + CSS + JS glue + <canvas> + UI 控件              │
└─────────────────────────────────────────────────────────┘
                          ↕ WebGL2 / WebAudio / Gamepad API
┌─────────────────────────────────────────────────────────┐
│ Layer 4: KNI Framework (MonoGame Web Fork)              │
│   NativeGL/WebGL2 后端（绕过 BlazorGL）                  │
│   SpriteBatch / GraphicsDevice / Content Pipeline       │
└─────────────────────────────────────────────────────────┘
                          ↕ .NET API
┌─────────────────────────────────────────────────────────┐
│ Layer 3: Game Assemblies (用户 GOG 副本，未修改)         │
│   Stardew Valley.dll                                    │
│   StardewValley.GameData.dll                            │
│   StardewModdingAPI.dll (Phase 3)                       │
└─────────────────────────────────────────────────────────┘
                          ↕ IL / Reflection
┌─────────────────────────────────────────────────────────┐
│ Layer 2: SMAPI Adapter (Phase 3)                        │
│   Harmony → RuntimeDetour 兼容层                        │
│   MonoMod.RuntimeDetour (替代 Harmony 后端)             │
│   Assembly.Load 适配层                                  │
└─────────────────────────────────────────────────────────┘
                          ↕
┌─────────────────────────────────────────────────────────┐
│ Layer 1: .NET 10 WASM Runtime                           │
│   Uno.Wasm.Bootstrap (Mixed-Mode + Jiterpreter)         │
│   OPFS / File System Access API 虚拟 FS                 │
└─────────────────────────────────────────────────────────┘
```

### 3.1 分层职责

| 层 | 职责 | 不可越界 |
|---|---|---|
| L5 浏览器 UX | Canvas 渲染 + 用户交互 | 不直接调 .NET |
| L4 KNI | MonoGame API → WebGL2 翻译 | 不依赖 SMAPI |
| L3 游戏程序集 | 原始代码执行 | 不感知下层实现 |
| L2 SMAPI 适配 | 让 SMAPI 在 WASM 跑通 | 不修改游戏 DLL |
| L1 运行时 | 提供 .NET WASM 执行环境 | 不感知游戏逻辑 |

---

## 4. 文件来源：A2 直读 + A1 OPFS 降级

### 4.1 主路径（A2：File System Access API 直读）

```
[用户点击"选择 GOG 安装目录"]
        ↓
[showDirectoryPicker()] ← Chrome/Edge 全支持
        ↓
[浏览器返回 FileSystemDirectoryHandle]
        ↓
[持久化 handle 到 IndexedDB（permission 持久化）]
        ↓
[运行时直接读 GOG 原始目录]
        ↓
[零拷贝、零等待、零存储占用]
```

**优点**：
- 零拷贝，460MB 文件不复制
- 用户改 GOG 目录内容立即生效（如换 mod）
- 不占 OPFS 配额

**缺点**：
- 仅 Chrome/Edge 全支持（约 60-70% 用户）
- 每次启动需重新授权（W3C 规范限制）

### 4.2 降级路径（A1：OPFS 上传）

```
[A2 不支持 / 用户拒绝目录授权]
        ↓
[提示拖入 GOG 安装目录的 ZIP 或目录结构]
        ↓
[流式分块上传到 OPFS（带进度条）]
        ↓
[持久化到 OPFS，二次访问零等待]
        ↓
[运行时从 OPFS 读]
```

**优点**：
- Firefox/Safari 兼容
- OPFS 持久化，二次访问零成本
- 比 IndexedDB 快 3 倍

**缺点**：
- 首次 460MB 上传 30-60 秒
- 占 OPFS 配额

### 4.3 第三兜底（不支持的浏览器）

提示用户使用 Chrome/Edge 或 Firefox 111+，不阻塞项目主线。

### 4.4 虚拟文件系统抽象

```csharp
public interface IVirtualFileSystem {
    // 同步 API（面向游戏代码的同步 File.OpenRead 调用）
    // 实现内部用阻塞式 await 异步后端（OPFS/FSA），
    // 需在 WASM 单线程上用 SyncWorkerHandler 实现伪同步
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    bool Exists(string path);
    long GetFileSize(string path);
    IEnumerable<string> EnumerateFiles(string pattern);
    IEnumerable<string> EnumerateDirectories(string path);

    // 异步 API（供运行时 / SMAPI / 启动器使用）
    Task<Stream> OpenReadAsync(string path);
    Task<bool> ExistsAsync(string path);
}

// 两条实现
class FileSystemAccessApiVfs : IVirtualFileSystem { ... }  // A2，JS interop 到 navigator.storage.getDirectory
class OpfsVfs : IVirtualFileSystem { ... }                  // A1，JS interop 到 OPFS
```

**关键设计点**：SDV 内部用同步 `File.OpenRead`，但 OPFS/FSA 都是异步。需用 `Uno.Wasm.Bootstrap` 的 `SyncWorkerHandler` 或在 Pthread 模式下用同步 OPFS API（Chrome 102+ 支持 `createSyncAccessHandle`）桥接。

游戏代码只见 `IVirtualFileSystem`，不感知实际后端。

---

## 5. 运行时层（L1）

### 5.1 技术选型

| 组件 | 选择 | 理由 |
|---|---|---|
| .NET 版本 | .NET 10 (LTS, 2025-11) | WASM 改进最完整，Jiterpreter 成熟 |
| WASM 引导器 | Uno.Wasm.Bootstrap | 细粒度运行时模式控制，KNI 已验证 |
| 执行模式 | Mixed-Mode（Interpreter + AOT + Jiterpreter） | 兼顾启动速度与运行性能 |
| AOT 范围 | KNI/游戏冷启动代码 | SMAPI 全 interpreter 保证 IL Emit 兼容 |
| 内存上限 | 4GB（启用 `--max-memory=4GB`） | SDV PC 版占 1.2GB + 运行时开销 |

### 5.2 性能预期

| 场景 | 预期 FPS（Mixed-Mode + Jiterpreter） | 说明 |
|---|---|---|
| 标题界面 | 45-60 | 几乎无逻辑负载 |
| 室内场景 | 35-50 | 单房间渲染 |
| 农场场景 | 20-35 | 大地图 + NPC 路径，最低验收线 20 FPS |
| 矿洞战斗 | 15-30 | 大量动态实体，最低验收线 15 FPS |

注：以上为 .NET 10 Mixed-Mode + Jiterpreter 模式下的预期。若启用全 AOT（SMAPI Phase 3 不可用），性能可提升 30-50%。

### 5.3 内存与配置

| 项 | 值 | 说明 |
|---|---|---|
| WASM 内存上限 | 4GB（桌面 Chrome/Edge） | 通过 `--max-memory=4GB` 启用 |
| iOS Safari 上限 | 2GB（系统限制） | 移动端需走精简路径，但移动端为非目标 |
| Firefox 桌面上限 | 2GB（默认） / 4GB（about:config） | 需文档提示用户调整 |
| 实际占用预期 | 1.5-2.5GB | SDV 1.2GB + 运行时 + KNI 开销 |
| 启用 WASM Threads | 是 | KNI WebGL 后端需 threading 支持 |
| 启用 SIMD | 视浏览器 | 自动降级 |

### 5.4 关键配置示例

```xml
<!-- Uno.Wasm.Bootstrap 的 csproj 关键参数 -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
  <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
  <WasmShellEnableJiterpreter>true</WasmShellEnableJiterpreter>
  <WasmShellIndexHtmlPath>wwwroot/index.html</WasmShellIndexHtmlPath>
  <WasmShellOPFSEnabled>true</WasmShellOPFSEnabled>
  <WasmShellFileDescriptorsEnabled>true</WasmShellFileDescriptorsEnabled>
</PropertyGroup>
```

注：Uno.Wasm.Bootstrap 使用 `WasmShell*` 前缀的属性，与 Blazor WASM 的 `Wasm*` 前缀不同。具体参数名以 [Uno.Wasm.Bootstrap 文档](https://github.com/unoplatform/Uno.Wasm.Bootstrap) 为准。

---

## 6. 渲染层（L4）：KNI + 原生 WebGL2

### 6.1 为何绕过 BlazorGL

社区报告显示 KNI 的 BlazorGL 后端在真实游戏中帧率低至 0.2 FPS。原因：BlazorGL 在 KNI 与 WebGL2 之间多了一层抽象。

### 6.2 推荐后端

**KNI 的 `WebGL` 后端**（KNI 自带，非 Silk.NET）：
- KNI 在 `MonoGame.Framework.WebGL` 程序集中实现 WebGL2 后端
- 通过 JS interop 直接调用浏览器 WebGL2 API
- 避免 BlazorGL 中间层（BlazorGL 是更早的实验性后端，被社区报告 0.2 FPS）

### 6.3 渲染管线

```
MonoGame SpriteBatch.Draw()
        ↓
KNI MonoGame.Framework.WebGL 后端
        ↓
JS Interop 调用 WebGL2 API
        ↓
WebGL2 context (Canvas)
        ↓
浏览器渲染
```

### 6.4 渲染优化后备方案

如果 Phase 1 末帧率低于 15 FPS：

- 启用 KNI 的 `IsFullScreen` + `SynchronizeWithVerticalRetrace` 默认优化
- 关闭 `PreferMultiSampling`（SDV 不需要 MSAA）
- 用 `BlendState.Opaque` 替代默认 AlphaBlend（适用场景下）
- 启用 WebGL2 的 `discardFramebuffer` 扩展
- 终极方案：在 KNI 层实现 dirty rectangle 优化（SDV 大部分时间屏幕静止）

---

## 7. SMAPI 移植路线（L2，Phase 3）

### 7.1 移植策略总览

采用 6 大策略削减工程代价：

| # | 策略 | 削减效果 |
|---|---|---|
| S1 | upstream PR 路线（不做长期 fork） | 长期维护 → 0 |
| S2 | 从 Android 移植版 fork 起点 | Phase 3 工作量 6-10 周 → 4-6 周 |
| S3 | 写 Harmony → RuntimeDetour 兼容 shim | mod 兼容测试矩阵爆炸 → 单一 shim 测试 |
| S4 | 分层 mod 兼容性（T1-T4） | 测试覆盖范围聚焦 |
| S5 | 自动化 mod 冒烟测试管线 | 手测 → 全自动 |
| S6 | 早期开源 + 社区共建 | 单人维护 → 社区共建 |

### 7.2 Phase 3 分阶段

```
Phase 3a (1 周): SMAPI 在 WASM 启动成功
  - 从 SMAPI Android fork 起点
  - 跑通 SMAPI 自启动日志
  - 验证基础 mod 加载管线

Phase 3b (3-4 周): Harmony → RuntimeDetour 兼容 shim
  - 实现 Harmony API → RuntimeDetour 翻译
  - 覆盖 Prefix/Postfix/Transpiler/Reverse Patch
  - 用 5 个最简单 mod 验证
  - 目标: T1 兼容率 90%+
  - 注: Harmony API 表面积大，估算 3-4 周，预留 buffer

Phase 3c (1 周): Top 20 mod 兼容性测试
  - 修复发现的问题
  - 同步提 PR 给 SMAPI upstream

Phase 3d (持续): 自动化测试管线 + 社区
  - CI 跑 NexusMods Top 100 mod
  - 生成兼容性报告
```

### 7.3 Mod 兼容性分层

| Tier | 类型 | 预期兼容率 | 测试投入 |
|---|---|---|---|
| T1 | 纯 SMAPI API mod | 95%+ | 自动化冒烟 |
| T2 | 简单 Harmony mod（Prefix/Postfix） | 80%+ | Top 20 手测 |
| T3 | 复杂 Harmony mod（Transpiler/IL Emit） | 30-50% | 标"实验性" |
| T4 | P/Invoke mod | ~0% | 不支持 |

### 7.4 关键技术决策

**Harmony 替代：MonoMod.RuntimeDetour**
- Android 移植已验证此路线
- WASM 上 Mixed-Mode + Jiterpreter 提供部分 JIT 能力，RuntimeDetour 通过 IL 重写 + JIT patch 实现
- 风险：WASM 沙箱下 IL Emit 行为未完全验证，需 Phase 3a 早期实验确认

**Assembly.Load 适配**
- WASM 上 `Assembly.Load(byte[])` 受限
- 写适配层：把 mod DLL 读为 byte[]，用 `AssemblyLoadContext.LoadFromStream` 加载
- Mono.Cecil 用于读 mod 元数据，不修改原 IL

### 7.5 SMAPI Fork 维护策略

- 主线：每次 SMAPI upstream release 后 rebase
- diff 控制在 1000 行以内（用 `git diff --stat` 监控）
- 同步推 PR 给 upstream，争取正式合并
- 失败兜底：维持小 diff fork，每次 release rebase 1-2 小时

---

## 8. XNB 编辑工作流（Phase 5）

### 8.1 XNB 格式背景

XNB 是 XNA Content Pipeline 的二进制资源格式，SDV 用它存图集、对话、事件、地图等。社区已有成熟工具 `xnbcli` 解包。

### 8.2 工作流设计

```
[PC 端: xnbcli 解包 .xnb → JSON]
        ↓
[用户编辑 JSON（任何文本编辑器）]
        ↓
[xnbcli 重打包 JSON → .xnb]
        ↓
[注入: OPFS 或原 GOG 目录]
        ↓
[浏览器启动游戏，加载修改后的资源]
```

### 8.3 集成方式

- 不在浏览器内实现 XNB 编辑器（YAGNI）
- 提供命令行脚本：`scripts/xnb-extract.sh` / `scripts/xnb-pack.sh`
- 文档说明如何用 OPFS 注入修改后的 XNB

### 8.4 验证方式

修改一个对话文件 → 重打包 → 启动游戏 → 验证对话内容已变更。

---

## 9. 分阶段里程碑

### Phase 0: 项目骨架（3-5 天）

**目标**：浏览器黑屏 + WASM 运行时跑通 + 1 帧任意颜色渲染成功

**验收**（最小验证，不接 KNI）：
- [ ] dotnet 10 + Uno.Wasm.Bootstrap 项目可构建
- [ ] 浏览器加载 WASM 包不报错
- [ ] Canvas 显示一帧指定颜色（用最简 JS interop，不依赖 KNI）
- [ ] WASM runtime 日志可输出到浏览器 console

KNI 后端初始化挪到 Phase 1 开始阶段验证。

### Phase 1: 标题界面（1-2 周）

**目标**：浏览器渲染 SDV 标题界面

**验收**：
- [ ] 用户能上传/直读 GOG 副本
- [ ] Content/*.xnb 资源加载成功
- [ ] 字体渲染正常
- [ ] Chucklefish logo 动画播放
- [ ] 标题菜单可见
- [ ] 帧率 ≥ 25 FPS（标题界面）

**风险点**：帧率 < 25 FPS（标题界面）或 < 15 FPS（任意场景）→ 触发渲染优化任务，参见 §10 R1

### Phase 2: 进游戏世界（2-4 周）

**目标**：从标题界面新建角色 → 进入农场场景

**验收**：
- [ ] 角色创建界面可用
- [ ] 存档读写（OPFS）正常
- [ ] 农场场景渲染
- [ ] 基础交互（移动、采集）可用
- [ ] 帧率 ≥ 20 FPS（农场场景）

### Phase 3: SMAPI 移植（4-6 周）

**目标**：浏览器内 SMAPI 可加载 mod

**验收**：
- [ ] SMAPI 启动日志在浏览器控制台可见
- [ ] T1 mod 兼容率 ≥ 90%（5 个测试 mod）
- [ ] T2 mod 兼容率 ≥ 80%（Top 20 mod）
- [ ] Harmony shim 单元测试覆盖率 ≥ 80%

### Phase 4: 第一个 mod 端到端验证（1-2 周）

**目标**：一个流行 mod（如 CJB Cheats）完整可用

**验收**：
- [ ] mod 加载无错误
- [ ] mod 功能在游戏内可触发
- [ ] 与无 mod 版本性能对比 ≤ 30% 下降

### Phase 5: XNB 编辑工作流（1 周）

**目标**：用户能修改 XNB 资源并看到效果

**验收**：
- [ ] xnbcli 集成脚本可用
- [ ] 修改对话文件 → 游戏内生效
- [ ] 文档完整

---

## 10. 风险登记册

| # | 风险 | 概率 | 影响 | 缓解措施 | 触发条件 |
|---|---|---|---|---|---|
| R1 | KNI+WebGL2 实测帧率 < 15 FPS | 30% | Phase 1 末追加 1-2 周渲染优化 | Phase 0 末做 24h PoC | Phase 1 验收时帧率不达标 |
| R2 | SMAPI 在 WASM 完全跑不通 | 25% | Phase 3 取消，项目降级为"无 mod 浏览器版" | Phase 1 完成后立即做 SMAPI PoC | Phase 3a 验收失败 |
| R3 | .NET 10 WASM 工具链 bug 阻塞 | 20% | 任意阶段卡 1-2 周 | 锁定具体版本，可降到 .NET 9 | 出现 dotnet/runtime 已知 issue |
| R4 | 内存超 2GB 在低端桌面设备崩溃 | 15% | 低端桌面设备不可用，文档明确最低配置 | 桌面端优先；iOS Safari 2GB 上限是已知约束，移动端为非目标 | OOM 报告 |

### 10.1 Phase 0 末的 PoC 验证清单

为降低 R1+R2，Phase 0 末额外做：

1. **渲染 PoC**：跑一个开源 MonoGame 示例游戏（如 [Nez samples](https://github.com/prime31/Nez)），测帧率
2. **SMAPI PoC**：尝试加载 `StardewModdingAPI.dll` 到 WASM，看启动日志能否输出（不要求 hook 成功）

**部分失败的决策矩阵**：

| 渲染 PoC | SMAPI PoC | 决策 |
|---|---|---|
| 通过 | 通过 | 按原计划进入 Phase 1+2+3 |
| 通过 | 失败 | 项目降级为"无 mod 浏览器版"，仍可交付；放弃 Phase 3-4，只做 Phase 1+2+5 |
| 失败 | 通过 | 渲染优化追加 1-2 周后重测；仍不达标则停止 |
| 失败 | 失败 | 停止项目，重新评估技术选型 |

---

## 11. 验收标准

### 11.1 项目级验收

| 维度 | 标准 |
|---|---|
| 法律姿态 | 仅本地/内网部署，无公网访问入口 |
| 功能完整 | Phase 0-5 全部验收通过 |
| 性能 - 标题界面 | ≥ 25 FPS（桌面端 Chrome） |
| 性能 - 农场场景 | ≥ 20 FPS（桌面端 Chrome） |
| 性能 - 矿洞战斗 | ≥ 15 FPS（桌面端 Chrome） |
| Mod 兼容 | Top 20 mod 中 T1+T2 ≥ 80% 可用 |
| 文档 | README + 部署指南 + 开发指南 + mod 兼容矩阵 |

### 11.2 不验收项

- 移动端性能
- T3/T4 mod 兼容
- 联机功能
- 60 FPS 极致性能

---

## 12. 非目标重申

- 不做 SDV 联机
- 不做移动端原生 app
- 不做云存档同步
- 不做 mod 浏览器/商店
- 不做游戏内 UI 重设计
- 不支持 T4（P/Invoke）mod
- 不追求 60 FPS
- 不公开部署

---

## 13. 项目目录结构（预定）

```
/home/z/my-project/
├── docs/
│   ├── superpowers/specs/          # 设计文档（本文件）
│   └── superpowers/plans/          # 实施计划（writing-plans skill 产出）
├── src/
│   ├── SdvWebPort.Runtime/          # Uno.Wasm.Bootstrap 主项目
│   ├── SdvWebPort.Vfs/              # 虚拟文件系统抽象
│   ├── SdvWebPort.SMAPI/            # SMAPI 适配层 + Harmony shim
│   └── SdvWebPort.Web/              # 浏览器 UX（HTML/JS glue）
├── smapi-fork/                      # SMAPI fork（S1 策略，小 diff）
├── vendor/                          # 第三方依赖（KNI、Uno.Wasm.Bootstrap 等 NuGet 缓存）
├── scripts/
│   ├── xnb-extract.sh
│   ├── xnb-pack.sh
│   └── smapi-mod-smoke-test.sh
├── tests/
│   ├── harmony-shim-tests/
│   └── mod-compat-matrix/
├── README.md
├── LICENSE                          # MIT（项目自身代码）
├── .gitignore
└── download/                        # 用户可下载交付物
```

---

## 14. 待确认事项（给 Review 者）

1. **是否接受"激进 SMAPI 路线"**（策略 S1-S6 全启用）？
2. **是否接受 25-40 FPS 性能区间**（不追求 60 FPS）？
3. **是否接受仅桌面端 Chrome/Edge 全支持**（Firefox/Safari 降级路径）？
4. **是否接受 Phase 0 末 PoC 失败时的"停止项目"决策**？
5. **项目目录结构是否需要调整**？

---

**End of design document**
