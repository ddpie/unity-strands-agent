# Unity AI Agent 插件

基于Strands Agent SDK和AWS Bedrock的Unity AI智能助手插件。

## 功能特性

- 🤖 **AI聊天界面** - 在Unity编辑器内进行自然语言交互
- 🔄 **流式响应** - 实时AI响应，打字机动画效果
- 🐍 **Python集成** - 通过Python.NET嵌入Python运行时
- 🔐 **AWS认证** - 使用本地AWS凭证（无需API密钥）
- 📦 **简易安装** - Unity Package Manager支持
- 🎯 **零配置** - 开箱即用，引导式设置

## 系统要求

- Unity 2022.3 LTS或更高版本
- macOS 10.15+
- Python 3.10+（自动检测）
- 本地配置的AWS凭证

## 安装方式

### 前置要求：安装Python.NET

1. 下载Python.Runtime.dll：
   ```bash
   # 方法1：通过curl下载
   curl -L -o pythonnet.zip https://www.nuget.org/api/v2/package/pythonnet/3.0.3
   unzip pythonnet.zip
   # DLL位置：lib/netstandard2.0/Python.Runtime.dll
   
   # 方法2：通过NuGet网站
   # 访问 https://www.nuget.org/packages/pythonnet/
   ```

2. 将`Python.Runtime.dll`复制到`Editor/Plugins/`目录

### 通过Unity Package Manager（本地）

1. 克隆仓库：
   ```bash
   git clone https://github.com/yourusername/unity-ai-agent.git
   ```

2. 在Unity中，打开Package Manager（Window → Package Manager）

3. 点击"+"按钮 → "Add package from disk..."

4. 导航到克隆的文件夹并选择`package.json`

### 通过Git URL

1. 在Unity Package Manager中，点击"+" → "Add package from git URL..."

2. 输入：`https://github.com/yourusername/unity-ai-agent.git`

3. 安装后，记得添加Python.Runtime.dll到`Editor/Plugins/`目录

## 快速开始

1. **打开设置向导**
   - Window → AI Assistant → Setup Wizard
   - 向导将引导您完成初始设置

2. **自动设置**
   - Python检测
   - 虚拟环境创建
   - 依赖安装
   - 全程实时进度反馈

3. **开始聊天**
   - Window → AI Assistant → Chat
   - 输入您的问题并获得AI帮助

## 使用方法

### 基本聊天

```
您：如何在Unity中旋转对象？

AI：在Unity中旋转对象，您可以使用以下几种方法：

1. 使用transform.Rotate()：
```csharp
transform.Rotate(0, 90, 0); // 在Y轴上旋转90度
```

2. 使用Quaternion：
```csharp
transform.rotation = Quaternion.Euler(0, 90, 0);
```
```

### 功能特性

- **Markdown支持** - 格式化文本和代码块
- **语法高亮** - 代码片段会高亮显示
- **聊天历史** - 对话自动保存
- **复制消息** - 轻松复制AI回复
- **日志查看器** - 调试Python和Unity集成

## 配置说明

插件使用`which python3`进行动态Python检测，无需手动配置！

### 手动Python路径（可选）

如果自动检测失败，您可以手动设置Python路径：

1. 创建`Assets/StreamingAssets/Plugins/python_config.json`：
```json
{
  "pythonPath": "/usr/local/bin/python3"
}
```

## 故障排除

### Python未找到

1. 确保安装了Python 3.10+：
   ```bash
   python3 --version
   ```

2. 如需要，通过Homebrew安装：
   ```bash
   brew install python@3.11
   ```

### AWS凭证

1. 配置AWS CLI：
   ```bash
   aws configure
   ```

2. 或设置环境变量：
   ```bash
   export AWS_ACCESS_KEY_ID=your_key
   export AWS_SECRET_ACCESS_KEY=your_secret
   export AWS_DEFAULT_REGION=us-east-1
   ```

### 虚拟环境问题

插件会在项目的`Python/venv/`目录创建虚拟环境。如果遇到问题：

1. 删除`Python/venv`文件夹
2. 重启Unity
3. 重新运行设置向导

## 开发说明

### 项目结构

```
UnityAIAgent/
├── Editor/
│   ├── AIAgentWindow.cs      # 主聊天窗口
│   ├── PythonManager.cs      # Python运行时管理
│   ├── SetupWizard.cs        # 首次设置UI
│   ├── StreamingHandler.cs   # 流式响应处理
│   ├── PythonBridge.cs       # Python.NET桥接
│   └── LogWindow.cs          # 调试日志查看器
├── Python/
│   ├── agent_core.py         # Strands Agent封装
│   ├── streaming_agent.py    # 流式响应支持
│   └── requirements.txt      # Python依赖
└── package.json              # Unity包清单
```

### 基本用法

1. **打开AI助手**
   ```
   Unity菜单 → Window → AI助手 → 聊天
   ```

2. **编程集成**
   ```csharp
   // 发送消息给AI
   string response = PythonBridge.ProcessMessage("你好，AI！");
   
   // 流式处理
   var handler = new StreamingHandler();
   await handler.StartStreaming("写一个Unity脚本");
   ```

### 添加自定义工具

1. 编辑`Python/agent_core.py`，添加工具：
```python
from strands import tool

@tool
def create_unity_object(name: str, x: float, y: float, z: float) -> str:
    """在Unity中创建游戏对象"""
    return f"已创建对象: {name} 在位置 ({x}, {y}, {z})"
```

2. 重启Unity使更改生效

## 许可证

MIT许可证 - 详见LICENSE文件

## 贡献

1. Fork仓库
2. 创建功能分支（`git checkout -b feature/AmazingFeature`）
3. 提交更改（`git commit -m 'Add some AmazingFeature'`）
4. 推送到分支（`git push origin feature/AmazingFeature`）
5. 打开Pull Request

## 支持

- 在GitHub上创建issue
- 查看[文档](https://docs.unity-ai-agent.com)
- 加入我们的Discord社区

## 致谢

- [Strands Agent SDK](https://strandsagents.com) - AI代理框架
- [Python.NET](https://github.com/pythonnet/pythonnet) - Python集成
- [AWS Bedrock](https://aws.amazon.com/bedrock/) - AI模型提供商