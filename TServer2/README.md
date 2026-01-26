# TServer2 - 德州扑克服务端项目结构

## 项目架构

```
TServer2/
├── Program.cs                      # 程序入口
├── TServer2.csproj                 # 项目配置
│
├── Model/                          # 核心数据模型
│   ├── Card.cs                     # 扑克牌（花色、点数）
│   ├── Player.cs                   # 玩家对象
│   ├── Pot.cs                      # 底池对象
│   └── GameEnums.cs                # 游戏枚举（阶段、牌型等）
│
├── Protocol/                       # 通信协议
│   ├── ClientMessage.cs            # 客户端消息定义
│   └── ServerMessage.cs            # 服务端消息定义（含所有Payload）
│
├── Network/                        # 网络层
│   ├── TcpGameServer.cs            # TCP服务器
│   └── ClientSession.cs            # 客户端会话管理
│
├── Game/                           # 游戏逻辑层
│   ├── Deck.cs                     # 牌组管理
│   ├── HandEvaluator.cs            # 手牌评估器
│   ├── BettingRound.cs             # 下注轮管理
│   ├── PotManager.cs               # 底池管理（含边池逻辑）
│   └── GameStateMachine.cs         # 游戏状态机（核心）
│
├── Controller/                     # 控制器层
│   └── GameRoomController.cs       # 游戏房间控制器
│
└── Logging/                        # 日志层
    └── Logger.cs                   # 控制台+文件日志
```

## 核心类说明

### 1. Model 层

#### Card
- `Suit`: 花色（梅花/方块/红心/黑桃）
- `Rank`: 点数（2-A）
- 提供 `CardDto` 用于JSON传输

#### Player
- 玩家基本信息：Id、Name、SeatIndex
- 筹码相关：Chips、CurrentBet、TotalBetThisHand
- 状态：HasFolded、IsAllIn、IsConnected
- 手牌：HoleCards

#### GameEnums
- `GamePhase`: 游戏阶段（等待/倒计时/PreFlop/Flop/Turn/River/Showdown/结算/结束）
- `HandRank`: 牌型（高牌到皇家同花顺，共10级）
- `HandEvaluation`: 手牌评估结果

### 2. Protocol 层

#### ClientMessage
```json
{
  "type": "JoinRoom|PlayerAction|ShowCards|MuckCards|Heartbeat",
  "playerName": "string",
  "action": "Fold|Check|Call|Bet|Raise|AllIn",
  "amount": 100
}
```

#### ServerMessage
```json
{
  "type": "JoinSuccess|PlayerJoined|...",
  "payload": { ... }
}
```

### 3. Network 层

#### TcpGameServer
- 监听端口，接受客户端连接
- 管理所有ClientSession
- 事件驱动：OnClientConnected/OnMessageReceived/OnClientDisconnected

#### ClientSession
- 处理单个客户端连接
- 协议格式：`[4字节大端长度头][JSON Body]`
- 自动JSON序列化/反序列化

### 4. Game 层

#### HandEvaluator
- 评估7张牌中最佳5张组合
- 支持所有标准牌型判断
- 提供牌型比较方法

#### PotManager
- 收集下注并创建主池/边池
- 使用"洋葱算法"处理All-In边池
- 按牌力分配奖金（含零头分配规则）

#### BettingRound
- 管理单轮下注
- 计算可用行动（Fold/Check/Call/Bet/Raise/AllIn）
- 验证并执行玩家行动

#### GameStateMachine
- 核心状态机，管理整局游戏
- 处理：玩家加入/移除、发牌、下注、阶段转换、摊牌、结算
- 使用 `Lock` 保证线程安全
- 20秒行动超时自动弃牌

### 5. Controller 层

#### GameRoomController
- 协调网络层和游戏逻辑层
- 处理玩家加入/离开
- 管理倒计时启动
- 消息路由和广播

## 游戏流程

1. **等待玩家** - 接受连接，处理JoinRoom
2. **倒计时** - 4人到达后开始10秒倒计时
3. **游戏开始** - 固定座位顺序，随机选庄
4. **每手牌循环**:
   - 盲注（小盲2，大盲4）
   - 发底牌（2张/人）
   - PreFlop下注（从枪口位开始）
   - 翻牌（烧1亮3）+ 下注
   - 转牌（烧1亮1）+ 下注
   - 河牌（烧1亮1）+ 下注
   - 摊牌 + 分池
   - 庄位顺时针轮转
5. **游戏结束** - 少于4人时公布排行榜

## 关键配置

| 配置项 | 值 |
|-------|-----|
| 初始筹码 | 1000 |
| 小盲 | 2 |
| 大盲 | 4 |
| 最少玩家 | 4 |
| 最多玩家 | 10 |
| 行动超时 | 20秒 |
| 倒计时 | 10秒 |
| 默认端口 | 5000 |

## 启动服务器

```bash
cd TServer2
dotnet run [port]
```

## 客户端连接示例

1. 建立TCP连接到 `localhost:5000`
2. 发送 JoinRoom 消息:
```json
{
  "type": 0,
  "playerName": "Alice"
}
```
3. 接收 JoinSuccess 响应
4. 等待游戏开始，响应 ActionRequest

## 日志

- 控制台：使用Spectre.Console彩色输出
- 文件：`bin/Debug/net10.0/logs/game_YYYYMMDD_HHmmss.log`
