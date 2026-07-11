#!/usr/bin/env python3
"""Generate SDV architecture analysis PDF"""
import os
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
    HRFlowable, ListFlowable, ListItem
)
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase.pdfmetrics import registerFontFamily
from reportlab.platypus.doctemplate import PageTemplate, BaseDocTemplate
from reportlab.platypus.frames import Frame

# Fonts
FONT_DIR = '/usr/share/fonts'
pdfmetrics.registerFont(TTFont('NotoSerifSC', f'{FONT_DIR}/truetype/noto-serif-sc/NotoSerifSC-Regular.ttf'))
pdfmetrics.registerFont(TTFont('NotoSerifSC-Bold', f'{FONT_DIR}/truetype/noto-serif-sc/NotoSerifSC-Bold.ttf'))
pdfmetrics.registerFont(TTFont('SarasaMono', f'{FONT_DIR}/truetype/chinese/SarasaMonoSC-Regular.ttf'))
registerFontFamily('NotoSerifSC', normal='NotoSerifSC', bold='NotoSerifSC-Bold')

# Palette
TEXT_PRIMARY = colors.HexColor('#2a2a28')
TEXT_BODY = colors.HexColor('#3a3a38')
TEXT_MUTED = colors.HexColor('#6a6a68')
ACCENT = colors.HexColor('#e3ca7e')
BORDER = colors.HexColor('#3a3830')
TABLE_HEADER_BG = colors.HexColor('#3a3830')
TABLE_HEADER_TEXT = colors.HexColor('#e3ca7e')
CODE_BG = colors.HexColor('#f0efe9')
BODY_BG = colors.HexColor('#fafaf8')

styles = getSampleStyleSheet()
style_h1 = ParagraphStyle('H1', parent=styles['Heading1'], fontName='NotoSerifSC-Bold', fontSize=20, leading=28, textColor=TEXT_PRIMARY, spaceBefore=16, spaceAfter=10)
style_h2 = ParagraphStyle('H2', parent=styles['Heading2'], fontName='NotoSerifSC-Bold', fontSize=14, leading=20, textColor=TEXT_PRIMARY, spaceBefore=12, spaceAfter=6)
style_h3 = ParagraphStyle('H3', parent=styles['Heading3'], fontName='NotoSerifSC-Bold', fontSize=11, leading=16, textColor=ACCENT, spaceBefore=8, spaceAfter=4)
style_body = ParagraphStyle('Body', parent=styles['Normal'], fontName='NotoSerifSC', fontSize=10, leading=16, textColor=TEXT_BODY, spaceBefore=2, spaceAfter=2)
style_code = ParagraphStyle('Code', parent=styles['Normal'], fontName='SarasaMono', fontSize=8, leading=12, textColor=TEXT_PRIMARY, backColor=CODE_BG, leftIndent=8, rightIndent=8, spaceBefore=4, spaceAfter=4, borderColor=BORDER, borderWidth=0.5, borderPadding=6)
style_bullet = ParagraphStyle('Bullet', parent=style_body, leftIndent=20, bulletIndent=8, spaceBefore=1, spaceAfter=1)
style_table_cell = ParagraphStyle('TC', parent=style_body, fontSize=9, leading=13, spaceBefore=2, spaceAfter=2)
style_table_header = ParagraphStyle('TH', parent=style_body, fontSize=9.5, leading=13, fontName='NotoSerifSC-Bold', textColor=TABLE_HEADER_TEXT)

def P(text, style=style_body): return Paragraph(text, style)
def H1(text): return Paragraph(text, style_h1)
def H2(text): return Paragraph(text, style_h2)
def H3(text): return Paragraph(text, style_h3)
def Code(text):
    escaped = text.replace('&','&amp;').replace('<','&lt;').replace('>','&gt;').replace('\n','<br/>').replace(' ','&nbsp;')
    return Paragraph(escaped, style_code)
def Bullets(items):
    return ListFlowable([ListItem(Paragraph(i, style_bullet), value='•', leftIndent=20) for i in items], bulletType='bullet', bulletColor=ACCENT, bulletFontSize=10)
def make_table(data, col_widths=None):
    processed = []
    for i, row in enumerate(data):
        prow = []
        for cell in row:
            if isinstance(cell, str):
                prow.append(Paragraph(cell, style_table_header if i==0 else style_table_cell))
            else:
                prow.append(cell)
        processed.append(prow)
    t = Table(processed, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ('VALIGN',(0,0),(-1,-1),'TOP'),
        ('LEFTPADDING',(0,0),(-1,-1),6),
        ('RIGHTPADDING',(0,0),(-1,-1),6),
        ('TOPPADDING',(0,0),(-1,-1),5),
        ('BOTTOMPADDING',(0,0),(-1,-1),5),
        ('GRID',(0,0),(-1,-1),0.5,BORDER),
        ('BACKGROUND',(0,0),(-1,0),TABLE_HEADER_BG),
        ('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white, colors.HexColor('#f5f5f0')]),
    ]))
    return t
def hr(): return HRFlowable(width='100%', thickness=0.5, color=BORDER, spaceBefore=6, spaceAfter=6)
def spacer(h=6): return Spacer(1, h)

class DocTemplate(BaseDocTemplate):
    def __init__(self, filename, **kwargs):
        BaseDocTemplate.__init__(self, filename, **kwargs)
        margin = 20*mm
        frame = Frame(margin, margin+15*mm, A4[0]-2*margin, A4[1]-2*margin-15*mm, id='normal')
        self.addPageTemplates([PageTemplate(id='body', frames=frame, onPage=self._draw)])
    def _draw(self, canvas, doc):
        canvas.saveState()
        canvas.setFillColor(BODY_BG)
        canvas.rect(0,0,A4[0],A4[1],fill=1,stroke=0)
        canvas.setStrokeColor(ACCENT)
        canvas.setLineWidth(2)
        canvas.line(20*mm, A4[1]-15*mm, A4[0]-20*mm, A4[1]-15*mm)
        canvas.setFont('SarasaMono', 8)
        canvas.setFillColor(TEXT_MUTED)
        canvas.drawString(20*mm, A4[1]-12*mm, 'SDV ARCHITECTURE ANALYSIS // v1.0')
        canvas.drawRightString(A4[0]-20*mm, A4[1]-12*mm, '2026-07-11')
        canvas.setStrokeColor(BORDER)
        canvas.setLineWidth(0.5)
        canvas.line(20*mm, 15*mm, A4[0]-20*mm, 15*mm)
        canvas.drawString(20*mm, 10*mm, 'PERSONAL USE ONLY')
        canvas.drawRightString(A4[0]-20*mm, 10*mm, f'PAGE {doc.page}')
        canvas.restoreState()

def build():
    s = []
    
    s.append(H1('星露谷物语 — 系统工程化分析'))
    s.append(hr())
    s.append(P('<b>分析对象</b>：GOG 版 Stardew Valley v1.6.15.24356，反编译 950 个 C# 文件，357,432 行代码'))
    s.append(P('<b>分析目标</b>：理解游戏架构、资源管线、数据驱动设计、存档系统、Mod 机制，为 Web 移植提供工程化基础'))
    s.append(spacer(10))
    
    # Chapter 1
    s.append(H2('一、代码规模与结构'))
    s.append(make_table([
        ['指标', '数值', '说明'],
        ['源码文件', '950 个', '反编译自 SDV.dll'],
        ['总行数', '357,432 行', '约 36 万行 C# 代码'],
        ['命名空间', '69 个', '按功能模块划分'],
        ['最大文件', 'GameLocation.cs (17,914 行)', '地图/场景逻辑'],
        ['第二大', 'Game1.cs (16,742 行)', '游戏主类'],
        ['第三大', 'Event.cs (13,635 行)', '事件系统'],
        ['XNB 资源', '3,550 个', '15 个目录'],
        ['资源总大小', '544 MB', '纹理/音频/数据/地图'],
    ], col_widths=[40*mm, 50*mm, 60*mm]))
    s.append(spacer(6))
    s.append(H3('最大文件 Top 10'))
    s.append(make_table([
        ['文件', '行数', '职责'],
        ['GameLocation.cs', '17,914', '地图场景逻辑：NPC、物品、交互、事件'],
        ['Game1.cs', '16,742', '游戏主类：732 个静态字段，全局状态管理'],
        ['Event.cs', '13,635', '随机事件系统：婚礼、节日、天气事件'],
        ['Farmer.cs', '9,424', '玩家类：金钱、技能、背包、社交、装备'],
        ['Utility.cs', '8,001', '工具函数库：路径查找、随机、坐标转换'],
        ['Object.cs', '7,346', '物品类：放置物、机器、容器'],
        ['NPC.cs', '7,295', 'NPC 类：日程、对话、AI、关系'],
        ['MineCart.cs', '6,462', '矿车小游戏'],
        ['DebugCommands.cs', '6,041', '调试控制台命令'],
        ['MineShaft.cs', '4,990', '矿洞生成与逻辑'],
    ], col_widths=[50*mm, 25*mm, 75*mm]))
    
    s.append(PageBreak())
    
    # Chapter 2
    s.append(H2('二、三层 Game 类架构'))
    s.append(P('SDV 的游戏架构采用三层设计，支持本地多人分屏：'))
    s.append(Code('''┌─────────────────────────────────────┐
│  GameRunner : Game (KNI/MG)         │
│  ─ 游戏窗口管理                     │
│  ─ GraphicsDevice 管理               │
│  ─ 多实例调度 (本地分屏)             │
│  ─ Update/Draw 每帧调用所有实例      │
└──────────────┬──────────────────────┘
               │ 聚合 (List<Game1>)
               ▼
┌─────────────────────────────────────┐
│  Game1 : InstanceGame                │
│  ─ 732 个静态字段 (全局状态)         │
│  ─ 游戏逻辑 (Update/Draw/Save)      │
│  ─ 当前位置/玩家/菜单/小游戏         │
└──────────────┬──────────────────────┘
               │ 代理 (不继承 Game)
               ▼
┌─────────────────────────────────────┐
│  InstanceGame (非 Game 子类)         │
│  ─ 代理 GameRunner 的资源            │
│  ─ GraphicsDevice → GameRunner      │
│  ─ Content → GameRunner              │
│  ─ Window → GameRunner               │
│  ─ 支持多实例独立状态                 │
└─────────────────────────────────────┘'''))
    s.append(spacer(6))
    s.append(H3('设计意图'))
    s.append(Bullets([
        '<b>GameRunner</b> 是真正的 Game 子类，管理窗口和图形设备',
        '<b>Game1</b> 继承 InstanceGame（不继承 Game），通过代理访问 GameRunner 的资源',
        '<b>本地多人</b>：GameRunner 维护 List&lt;Game1&gt;，每个 Game1 是一个独立玩家实例',
        '<b>分屏渲染</b>：GameRunner.Draw 遍历所有实例，分别调用 Instance_Draw',
    ]))
    
    s.append(H2('三、全局状态管理 (Game1 静态字段)'))
    s.append(P('Game1 有 732 个静态字段，是整个游戏的全局状态中心。这是 SDV 架构的核心特点——所有游戏状态通过静态字段共享：'))
    s.append(make_table([
        ['类别', '关键字段', '说明'],
        ['核心管理器', 'graphics, content, spriteBatch', '图形/内容/精灵批处理'],
        ['游戏状态', 'player, currentLocation, gameMode', '当前玩家/位置/模式'],
        ['UI 状态', 'activeClickableMenu, currentMinigame, dialogueUp', '活动菜单/小游戏/对话'],
        ['纹理资源', 'mouseCursors, objectSpriteSheet, cropSpriteSheet', 'UI/物品/作物纹理'],
        ['字体', 'dialogueFont, smallFont, tinyFont', '三种字体大小'],
        ['输入', 'input (InputState)', '键盘/鼠标/手柄状态'],
        ['随机', 'random, recentMultiplayerRandom', '游戏随机数'],
        ['多人', 'multiplayer, multiplayerMode', '联机状态'],
        ['Mod', 'hooks (ModHooks)', 'SMAPI 钩子接口'],
        ['背景色', 'bgColor (CornflowerBlue)', '清屏颜色'],
    ], col_widths=[30*mm, 60*mm, 60*mm]))
    s.append(spacer(6))
    s.append(P('<b>gameMode 枚举</b>：0=标题画面, 2=加载中, 3=游戏中, 4=保存, 6=过渡, 10=??, 11=错误'))
    
    s.append(PageBreak())
    
    # Chapter 4
    s.append(H2('四、游戏循环 (Update/Draw)'))
    s.append(P('SDV 的游戏循环由 KNI 的 Game.Run() 驱动，每帧调用 Update 然后 Draw：'))
    s.append(Code('''Game.Run() (每帧循环)
    │
    ├─→ GameRunner.Update(gameTime)
    │       │
    │       ├─→ GameStateQuery.Update()        // 状态查询缓存更新
    │       ├─→ 处理 activeNewDayProcesses     // 新一天协程
    │       ├─→ 读取 GamePad 状态              // 手柄输入
    │       │
    │       └─→ for each gameInstance:
    │               ├─→ LoadInstance(game)      // 加载实例上下文
    │               ├─→ game.Instance_Update()  // 游戏逻辑更新
    │               │       │
    │               │       ├─→ input.UpdateStates()     // 输入采样
    │               │       ├─→ _update(gameTime)        // 主更新逻辑
    │               │       │       ├─→ 检查新一天任务
    │               │       │       ├─→ 处理客户端超时
    │               │       │       ├─→ updateCurrentMenu // 菜单更新
    │               │       │       ├─→ currentLocation.update() // 场景更新
    │               │       │       │       ├─→ NPC.update()
    │               │       │       │       ├─→ 对象.update()
    │               │       │       │       ├─→ 地形特征.update()
    │               │       │       │       └─→ 农场动物.update()
    │               │       │       ├─→ player.update()  // 玩家更新
    │               │       │       ├─→ updateWeather()   // 天气
    │               │       │       └─→ updateChatBox()   // 聊天
    │               │       └─→ base.Update()             // 组件更新
    │               └─→ SaveInstance(game)     // 保存实例上下文
    │
    └─→ GameRunner.Draw(gameTime)
            │
            └─→ for each gameInstance:
                    ├─→ LoadInstance(game)
                    ├─→ 设置 Viewport
                    ├─→ game.Instance_Draw()   // 渲染
                    │       │
                    │       └─→ _draw(gameTime)
                    │               ├─→ GraphicsDevice.Clear(bgColor)
                    │               ├─→ hooks.OnRendering(FullScene)
                    │               ├─→ if 加载中: DrawLoadScreen
                    │               ├─→ if 小游戏: currentMinigame.draw()
                    │               ├─→ DrawWorld()          // 世界渲染
                    │               │       ├─→ currentLocation.draw()  // 地图
                    │               │       ├─→ drawWeather()           // 天气
                    │               │       └─→ DrawHud()               // HUD
                    │               ├─→ DrawMenu()           // 菜单
                    │               └─→ hooks.OnRendered(FullScene)
                    └─→ SaveInstance(game)'''))
    
    s.append(PageBreak())
    
    # Chapter 5
    s.append(H2('五、渲染管线 (20 个 RenderSteps)'))
    s.append(P('SDV 的渲染管线有 20 个阶段，每个阶段都有 SMAPI Mod 钩子（OnRendering/OnRendered），允许 Mod 在任意阶段插入自定义渲染：'))
    s.append(make_table([
        ['阶段', 'RenderStep', '说明'],
        ['全场景', 'FullScene', '整个渲染流程的入口和出口'],
        ['菜单背景', 'MenuBackground', '菜单透明背景'],
        ['小游戏', 'Minigame', '小游戏渲染'],
        ['加载画面', 'LoadingScreen', '加载进度'],
        ['世界', 'World', '游戏世界主渲染'],
        ['世界-背景', 'World_Background', '远景层'],
        ['世界-排序', 'World_Sorted', '按 Y 排序的实体'],
        ['世界-天气', 'World_Weather', '雨/雪效果'],
        ['世界-光照', 'World_RenderLightmap', '光照贴图'],
        ['世界-光照上屏', 'World_DrawLightmapOnScreen', '光照贴图渲染'],
        ['世界-最前', 'World_AlwaysFront', '始终最前的效果'],
        ['HUD', 'HUD', '血量/体力/时钟'],
        ['对话框', 'DialogueBox', 'NPC 对话框'],
        ['菜单', 'Menu', '活动菜单'],
        ['覆盖层', 'Overlays', '所有覆盖层'],
        ['覆盖-菜单', 'Overlays_OverlayMenu', '覆盖菜单'],
        ['覆盖-聊天', 'Overlays_Chatbox', '聊天框'],
        ['覆盖-键盘', 'Overlays_OnscreenKeyboard', '屏幕键盘'],
        ['临时精灵', 'OverlayTemporarySprites', '临时动画'],
        ['全局淡入淡出', 'GlobalFade', '场景过渡'],
    ], col_widths=[25*mm, 50*mm, 75*mm]))
    s.append(spacer(6))
    s.append(P('<b>Mod 钩子机制</b>：每个阶段调用 hooks.OnRendering()（返回 false 可跳过原版渲染）和 hooks.OnRendered()（渲染后回调）。SMAPI 通过继承 ModHooks 类实现自定义渲染。'))
    
    s.append(PageBreak())
    
    # Chapter 6
    s.append(H2('六、资源管线 (XNB + ContentManager)'))
    s.append(H3('资源目录结构'))
    s.append(make_table([
        ['目录', '文件数', '内容', 'Web 移植处理'],
        ['Data', '851', '游戏数据定义 (JSON-like)', '✅ HttpVfs 直接加载'],
        ['Characters', '872', '角色精灵图', '✅ HttpVfs'],
        ['Strings', '732', '多语言文本', '✅ HttpVfs'],
        ['Maps', '563', 'xTile 地图文件', '✅ HttpVfs'],
        ['LooseSprites', '156', 'UI/特效纹理', '✅ HttpVfs'],
        ['Portraits', '101', 'NPC 肖像', '✅ HttpVfs'],
        ['Fonts', '50', 'SpriteFont 字体', '✅ HttpVfs'],
        ['TileSheets', '41', '物品/作物/工具表', '✅ HttpVfs'],
        ['Buildings', '47', '建筑纹理', '✅ HttpVfs'],
        ['Minigames', '53', '小游戏资源', '✅ HttpVfs'],
        ['Animals', '43', '动物精灵图', '✅ HttpVfs'],
        ['XACT', '5', '音频工程文件', '⚠️ 需 Web Audio 转换'],
    ], col_widths=[25*mm, 20*mm, 50*mm, 55*mm]))
    s.append(spacer(6))
    s.append(H3('XNB 文件格式'))
    s.append(Code('''XNB 文件结构:
┌──────────────────┐
│ Header (XNB + 版本) │
├──────────────────┤
│                  │
│  Content Data    │  ← 可以是 Texture2D, SpriteFont,
│                  │     Dictionary, List, 自定义类型
│                  │
└──────────────────┘

加载方式: content.Load<T>("path\\file")
  └─→ LocalizedContentManager 读取 XNB
      └─→ 反序列化为 T 类型
      └─→ 缓存 (ContentManager 内部字典)'''))
    s.append(spacer(6))
    s.append(H3('ContentManager 缓存机制'))
    s.append(P('ContentManager 内部维护一个字典缓存已加载的资源。同一个资源路径多次 Load 只读取一次 XNB 文件。'
        'Game1.cs 中有 54 处 content.Load 调用，加载纹理、字体、音效等。'
        '在我们的 Web 移植中，HttpVfs 替代了文件系统读取，但 ContentManager 缓存机制保持不变。'))
    
    s.append(PageBreak())
    
    # Chapter 7
    s.append(H2('七、数据驱动设计 (DataLoader)'))
    s.append(P('SDV 的核心设计理念是<b>数据驱动</b>——游戏逻辑由代码定义，但游戏内容由 XNB 数据文件定义。'
        'DataLoader 类是所有游戏数据的加载入口，有 40+ 个静态方法，每个对应一个 Data/*.xnb 文件：'))
    s.append(Code('''DataLoader 加载流程:

  DataLoader.Objects(content)
      │
      ▼
  content.Load<Dictionary<string, ObjectData>>("Data\\Objects")
      │
      ▼
  HttpVfs.OpenRead("Content/Data/Objects.xnb")
      │
      ▼
  XNB 反序列化 → Dictionary<string, ObjectData>
      │
      ▼
  Game1.objectData = result  (静态字段缓存)

  使用: Game1.objectData["0"] → ObjectData { Name="Stone", ... }'''))
    s.append(spacer(6))
    s.append(H3('关键 Data 文件与对应类型'))
    s.append(make_table([
        ['Data 文件', 'C# 类型', '内容'],
        ['Objects.xnb', 'Dictionary&lt;string, ObjectData&gt;', '所有物品定义'],
        ['Crops.xnb', 'Dictionary&lt;string, CropData&gt;', '作物生长规则'],
        ['Characters.xnb', 'Dictionary&lt;string, CharacterData&gt;', 'NPC 定义'],
        ['Buildings.xnb', 'Dictionary&lt;string, BuildingData&gt;', '建筑定义'],
        ['FarmAnimals.xnb', 'Dictionary&lt;string, FarmAnimalData&gt;', '农场动物'],
        ['Machines.xnb', 'Dictionary&lt;string, MachineData&gt;', '机器配方'],
        ['BigCraftables.xnb', 'Dictionary&lt;string, BigCraftableData&gt;', '大件物品'],
        ['Hats.xnb', 'Dictionary&lt;string, string&gt;', '帽子定义'],
        ['Boots.xnb', 'Dictionary&lt;string, string&gt;', '靴子定义'],
        ['Weapons.xnb', 'Dictionary&lt;string, string&gt;', '武器定义'],
        ['CookingRecipes.xnb', 'Dictionary&lt;string, string&gt;', '烹饪配方'],
        ['CraftingRecipes.xnb', 'Dictionary&lt;string, string&gt;', '制作配方'],
        ['Festivals_FestivalDates.xnb', 'Dictionary&lt;string, string&gt;', '节日日期'],
        ['NPCDispositions.xnb', 'Dictionary&lt;string, string&gt;', 'NPC 日程'],
    ], col_widths=[45*mm, 55*mm, 50*mm]))
    s.append(spacer(6))
    s.append(P('<b>对 Web 移植的意义</b>：数据驱动意味着游戏内容（物品、NPC、配方）完全由 XNB 文件定义，'
        '代码只是引擎。修改 XNB 就能改变游戏内容，无需修改代码——这正是 XNB 美化和汉化的基础。'))
    
    s.append(PageBreak())
    
    # Chapter 8
    s.append(H2('八、存档系统'))
    s.append(P('存档使用 XML 序列化，保存整个游戏状态：'))
    s.append(Code('''存档文件结构:
  Saves/
    └── <FarmerName>_<FarmId>/
        ├── SaveGameInfo           ← 摘要 (名字/日期/金钱)
        ├── <FarmerName>_<FarmId>  ← 完整 XML 存档
        └── <FarmerName>_<FarmId>_old  ← 备份

SaveGame 类包含:
  ├── List<GameLocation> locations    ← 所有地图场景
  ├── Farmer player                   ← 玩家完整状态
  ├── WorldState worldState           ← 世界状态
  ├── HashSet<string> worldStateIDs   ← 世界事件标记
  ├── SerializableDictionary friendships ← NPC 关系
  └── 各种游戏状态字段'''))
    s.append(spacer(6))
    s.append(H3('Farmer 类（玩家数据）'))
    s.append(P('Farmer.cs 有 9,424 行，683 个成员，包含玩家的所有状态：'))
    s.append(Bullets([
        '<b>金钱</b>：_money (NetInt，支持多人同步)',
        '<b>技能</b>：stats (Stats 类，含采矿/农耕/钓鱼/战斗/采集经验)',
        '<b>任务</b>：questLog (NetObjectList&lt;Quest&gt;)',
        '<b>社交</b>：friendshipData (NetStringDictionary&lt;Friendship&gt;)',
        '<b>已收邮件</b>：mailReceived (NetStringHashSet)',
        '<b>对话历史</b>：dialogueQuestionsAnswered (NetStringHashSet)',
        '<b>活跃对话事件</b>：activeDialogueEvents (NetStringDictionary&lt;int&gt;)',
        '<b>家位置</b>：homeLocation (NetString, 默认 "FarmHouse")',
        '<b>配偶</b>：spouse (string, NPC 名字)',
        '<b>坐骑</b>：mount (Horse 对象)',
    ]))
    s.append(spacer(6))
    s.append(P('<b>Net* 前缀类型</b>：SDV 使用 Netcode 库实现多人同步。NetInt、NetString 等类型在值变化时自动通知其他客户端。'
        '在 Web 移植中，单机模式不需要同步，但类型本身可以正常使用。'))
    s.append(spacer(6))
    s.append(P('<b>Web 移植存档方案</b>：使用 OPFS (Origin Private File System) 替代文件系统，XML 序列化保持不变。'
        '存档/读档通过 JS Interop 调用浏览器 File System API。'))
    
    s.append(PageBreak())
    
    # Chapter 9
    s.append(H2('九、Mod 系统 (SMAPI 架构)'))
    s.append(P('SDV 内置了 Mod 钩子机制（ModHooks 类），SMAPI 通过继承 ModHooks 实现所有 Mod 功能：'))
    s.append(Code('''ModHooks 基类 (SDV 内置):
  ├── OnRendering(step, sb, time)   ← 渲染前钩子
  ├── OnRendered(step, sb, time)    ← 渲染后钩子
  ├── OnGame1_PerformTenMinuteClockUpdate(action)
  ├── OnGame1_NewDayAfterFade(action)
  ├── OnGameLocation_ResetForPlayerEntry(loc, action)
  ├── OnGameLocation_CheckAction(loc, ...)
  ├── TryDrawMenu(menu, drawAction)
  ├── StartTask(task, id)
  └── CreatedInitialLocations()

SMAPI 的 SModHooks : ModHooks:
  ├── 重写所有 virtual 方法
  ├── 在方法中触发 SMAPI 事件
  ├── 事件分发给所有 Mod
  └── Mod 通过 Harmony 进一步 patch'''))
    s.append(spacer(6))
    s.append(H3('Mod 加载流程'))
    s.append(Bullets([
        '<b>1. SMAPI 启动</b>：Program.Main → SMAPI 入口 → 加载 Mods/ 目录',
        '<b>2. Mod 清单</b>：读取每个 mod 的 manifest.json',
        '<b>3. 依赖检查</b>：验证 mod 依赖和加载顺序',
        '<b>4. Mod 初始化</b>：创建 Mod 实例，调用 Entry(IPlayerHelper)',
        '<b>5. 事件订阅</b>：Mod 通过 helper.Events 订阅游戏事件',
        '<b>6. Harmony patch</b>：Mod 可使用 Harmony 运行时替换任意方法',
    ]))
    s.append(spacer(6))
    s.append(P('<b>源码编译模式下的 SMAPI 支持</b>：由于我们直接编译 SDV 源码，可以：'))
    s.append(Bullets([
        '直接修改 ModHooks 子类，内置 SMAPI 钩子',
        'Harmony 在源码编译的 DLL 上工作正常（不像 IL patch 模式有 JIT 限制）',
        '加载用户上传的 Mod DLL 到 AssemblyLoadContext',
        '支持 Content Patcher（XNB 替换 mod）',
    ]))
    
    s.append(PageBreak())
    
    # Chapter 10
    s.append(H2('十、Web 移植工程化要点'))
    s.append(H3('需要适配的子系统'))
    s.append(make_table([
        ['子系统', '原版实现', 'Web 适配方案', '难度'],
        ['文件系统', 'File.Open/Directory', 'HttpVfs + OPFS', '✅ 已完成'],
        ['图形', 'MonoGame DX', 'KNI WebGL2', '✅ 已完成'],
        ['音频', 'XACT (xgs/xsb/xwb)', 'Web Audio API 转换', '⚠️ 中等'],
        ['输入', 'Win32/Mac/SDL', '浏览器事件 + 虚拟控件', '⚠️ 中等'],
        ['线程', 'Task/Thread', 'WASM 单线程串行化', '⚠️ 中等'],
        ['存档', '文件系统 XML', 'OPFS + XML', '✅ 简单'],
        ['网络', 'Lidgren UDP', 'WebRTC P2P', '🔴 困难'],
        ['剪贴板', 'P/Invoke', 'JS Interop', '✅ 简单'],
        ['Mod', 'SMAPI + Harmony', '源码模式直接支持', '✅ 简单'],
    ], col_widths=[25*mm, 35*mm, 50*mm, 20*mm]))
    s.append(spacer(6))
    s.append(H3('XNB 资源美化支持'))
    s.append(P('由于 SDV 是数据驱动设计，XNB 美化非常简单：'))
    s.append(Bullets([
        '<b>纹理替换</b>：用户上传自定义 LooseSprites/*.xnb，VFS 优先加载用户版本',
        '<b>汉化</b>：用户上传 zh-CN 版本的 Strings/*.xnb 和 Data/*.xnb',
        '<b>地图编辑</b>：用户上传自定义 Maps/*.xnb',
        '<b>数据修改</b>：用户上传自定义 Data/*.xnb（修改物品/NPC/配方）',
        '<b>Content Patcher</b>：支持 SMAPI Content Patcher mod 格式',
    ]))
    s.append(spacer(6))
    s.append(H3('三模式支持架构'))
    s.append(Code('''用户上传选择:
  ┌─ 原版模式: 使用原始 GOG XNB 资源
  │
  ├─ XNB 美化模式: 用户上传自定义 XNB
  │   └─ VFS 优先加载用户 XNB, 缺失时 fallback 原版
  │
  └─ SMAPI Mod 模式: 用户上传 Mod DLL + Content
      ├─ AssemblyLoadContext 加载 Mod DLL
      ├─ Mod 通过 Harmony patch 游戏方法
      └─ Mod 通过 Content Patcher 替换 XNB'''))
    
    s.append(PageBreak())
    
    # Summary
    s.append(H2('十一、总结'))
    s.append(make_table([
        ['维度', '原版 SDV', 'Web 移植版', '兼容性'],
        ['代码', '357K 行 C#', '反编译+重编译', '100% 逻辑保留'],
        ['资源', '3550 XNB (544MB)', 'HttpVfs 加载', '100% 格式兼容'],
        ['渲染', 'MonoGame DX', 'KNI WebGL2', '20 个 RenderStep 全保留'],
        ['数据', 'DataLoader 40+ 方法', '原样保留', '数据驱动设计不变'],
        ['存档', 'XML 文件', 'OPFS + XML', '格式 100% 兼容'],
        ['Mod', 'SMAPI + Harmony', '源码模式直接支持', 'Harmony 可工作'],
        ['输入', '键鼠/手柄', '键鼠/触摸/虚拟控件', '统一 VirtualInput'],
        ['音频', 'XACT', 'Web Audio 转换', '需适配'],
        ['网络', 'Lidgren UDP', 'WebRTC (可选)', '需适配'],
    ], col_widths=[25*mm, 40*mm, 45*mm, 40*mm]))
    s.append(spacer(10))
    s.append(P('<b>核心结论</b>：SDV 的架构非常适合 Web 移植——数据驱动设计让内容修改（XNB 美化/汉化）无需改代码，'
        'ModHooks 机制让 SMAPI 支持只需继承一个类，源码编译模式彻底绕过 IL patch 的 JIT 限制。'
        '主要工作量在音频转换和网络联机（可选），其他子系统都已有清晰的适配方案。'))
    
    return s

output = '/home/z/my-project/download/SDV-系统工程化分析.pdf'
doc = DocTemplate(output, pagesize=A4, title='SDV 系统工程化分析', author='Z.ai')
doc.build(build())
print(f'[+] PDF: {output}')
print(f'[+] Size: {os.path.getsize(output)/1024:.1f} KB')
