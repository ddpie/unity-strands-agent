# References Documentation

## Strands Agent SDK

### 主要资源链接
- **官方文档**: https://strandsagents.com/latest/
- **Python SDK**: https://github.com/strands-agents/sdk-python
- **示例代码**: https://github.com/strands-agents/samples
- **工具包**: https://github.com/strands-agents/tools
- **Agent Builder**: https://github.com/strands-agents/agent-builder
- **文档源码**: https://github.com/strands-agents/docs

### 安装和基本使用

#### 环境要求
- Python 3.10+
- AWS凭证（使用Amazon Bedrock时需要）
- 模型提供商凭证（OpenAI、Anthropic等）

#### 安装
```bash
# 创建虚拟环境
python3 -m venv .venv
source .venv/bin/activate

# 升级pip
pip install --upgrade pip

# 安装核心包
pip install strands-agents
pip install strands-agents-tools  # 可选：预构建工具
pip install strands-agents-builder  # 可选：Agent构建器
```

#### 基本使用
```python
from strands import Agent
agent = Agent()
agent("Tell me about agentic AI")
```

### 核心特性

#### 1. Agent Loop（代理循环）
核心概念，通过以下阶段实现智能自主行为：
- 推理阶段
- 工具执行
- 响应生成
- 持续迭代

#### 2. 模型无关设计
支持多种提供商：
- Amazon Bedrock（默认）
- Anthropic
- OpenAI
- Ollama
- LiteLLM
- Llama
- 自定义提供商

#### 3. 工具系统
```python
from strands import Agent, tool

@tool
def letter_counter(word: str, letter: str) -> int:
    """Count occurrences of a specific letter in a word."""
    return word.lower().count(letter.lower())

# 创建带工具的代理
agent = Agent(tools=[letter_counter])
response = agent("How many times does 'e' appear in 'engineering'?")
```

### 文档结构

#### 用户指南
1. **快速入门**
   - 基本设置和第一个代理
   - 工具使用入门
   - 系统提示自定义

2. **概念介绍**
   - Agents：轻量级AI代理框架
   - Tools：扩展代理能力的机制
   - Model Providers：支持不同的模型提供商

3. **安全与安全性**
   - 负责任的AI开发
   - 工具执行沙箱
   - 凭证管理

4. **可观察性与评估**
   - OpenTelemetry集成
   - 分布式追踪
   - 指标收集

5. **部署选项**
   - AWS Lambda
   - AWS Fargate
   - Amazon EC2
   - 容器化部署

#### API参考
- Agent类
- Event Loop
- Handlers
- Models
- Tools

### Strands Agent Tools (v0.1.7)

**安装**: `pip install strands-agents-tools`

**功能特性**:

#### 1. 文件操作
```python
from strands_tools import file_read, file_write, editor

# 读取文件
content = agent.tool.file_read(path="config.json")

# 写入文件
agent.tool.file_write(path="output.txt", content="Hello, world!")

# 编辑器功能（语法高亮、智能修改）
agent.tool.editor(file="code.py", action="modify", ...)
```

#### 2. Shell集成
- 安全的命令执行
- 输出捕获
- 错误处理

#### 3. 内存系统
- Mem0集成
- Amazon Bedrock Knowledge Bases
- 持久化用户/代理记忆

#### 4. HTTP客户端
```python
from strands_tools import http_request

response = agent.tool.http_request(
    method="GET",
    url="https://api.example.com/data",
    headers={"Authorization": "Bearer token"}
)
```

#### 5. Python执行
- 代码片段执行
- 状态持久化
- 安全特性

#### 6. 高级功能
- Slack集成
- 群体智能
- 浏览器自动化
- 图像和视频处理
- 数学计算

### Strands Agent Builder

**安装**: 
```bash
# 使用Homebrew安装pipx（推荐）
brew install pipx
pipx ensurepath

# 或使用pip安装pipx
pip install --user pipx
pipx ensurepath

# 安装Strands Agent Builder
pipx install strands-agents-builder
```

**核心功能**:
- 创建自定义AI工具（即时热重载）
- 构建专门的代理
- 开发复杂的AI工作流
- 集成12+内置工具
- 知识库集成

**快速开始**:
```bash
# 运行交互模式
strands

# 创建自定义情感分析工具
strands "Create a tool named sentiment_analyzer that analyzes text sentiment"
```

### 示例项目详解

#### 1. 基础代理创建
最小化设置，使用默认配置：
```python
from strands import Agent
agent = Agent()
response = agent("Tell me about agentic AI")
```

#### 2. 多工具代理
组合多个工具增强能力：
```python
from strands import Agent
from strands_tools import calculator, current_time, python_repl

agent = Agent(tools=[calculator, current_time, python_repl])

message = """
1. What is the time right now?
2. Calculate 3111696 / 74088
3. Write a Python script that prints Fibonacci numbers
"""

agent(message)
```

#### 3. 多代理工作流
```python
from strands import Agent
from strands_tools import workflow

# 创建主协调器代理
agent = Agent(tools=[workflow])

# 创建多代理工作流
agent.tool.workflow(
    action="create",
    workflow_id="research_analysis",
    tasks=[
        {
            "task_id": "data_extraction",
            "description": "Extract key financial data from quarterly reports",
            "system_prompt": "You are a financial data extraction specialist.",
            "priority": 5
        },
        {
            "task_id": "trend_analysis",
            "dependencies": ["data_extraction"],
            "description": "Analyze trends in the extracted financial data",
            "priority": 3
        },
        {
            "task_id": "report_generation",
            "dependencies": ["trend_analysis"],
            "description": "Generate executive summary report",
            "priority": 2
        }
    ]
)

# 执行工作流
agent.tool.workflow(action="execute", workflow_id="research_analysis")
```

#### 4. MCP集成
访问数千个预构建工具：
```python
from strands import Agent
from strands.tools.mcp import MCPClient
from mcp import stdio_client, StdioServerParameters

# 创建AWS文档的MCP客户端
aws_docs_client = MCPClient(
    lambda: stdio_client(StdioServerParameters(
        command="uvx",
        args=["awslabs.aws-documentation-mcp-server@latest"]
    ))
)

# 使用MCP工具
with aws_docs_client:
    agent = Agent(tools=aws_docs_client.list_tools_sync())
    response = agent("Tell me about Amazon Bedrock and how to use it")
```

#### 5. 流式响应
```python
from strands import Agent
from strands.models import BedrockModel
import asyncio

# 创建支持流式的代理
streaming_model = BedrockModel(
    model_id="us.amazon.nova-pro-v1:0",
    streaming=True
)

agent = Agent(model=streaming_model)

# 异步流式示例
async def stream_response():
    async for chunk in agent.stream_async("Write a story about AI agents"):
        print(chunk, end="", flush=True)

asyncio.run(stream_response())
```

### 生产环境使用案例
- Amazon Q Developer
- AWS Glue
- VPC Reachability Analyzer

### 配置最佳实践

#### 日志配置
```python
import logging
logging.getLogger("strands").setLevel(logging.DEBUG)
logging.basicConfig(
    format="%(levelname)s | %(name)s | %(message)s",
    handlers=[logging.StreamHandler()]
)
```

#### 项目结构
```
my_agent/
├── __init__.py
├── agent.py
├── tools/
│   ├── __init__.py
│   └── custom_tools.py
├── config/
│   └── settings.py
├── requirements.txt
└── pyproject.toml
```

## C# 运行 Python

### 主要资源
- **Python.NET**: https://github.com/pythonnet/pythonnet
- **Wiki文档**: https://github.com/pythonnet/pythonnet/wiki
- **邮件列表**: https://mail.python.org/mailman/listinfo/pythondotnet
- **Gitter聊天**: https://gitter.im/pythonnet/pythonnet

### 项目概述
Python.NET提供Python与.NET CLR的无缝集成，是一个成熟的开源项目，由.NET Foundation支持。

### macOS安装方法

#### 前置要求
```bash
# 安装Xcode Command Line Tools
xcode-select --install

# 安装Homebrew（如果未安装）
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# 安装.NET SDK
brew install --cask dotnet

# 验证.NET安装
dotnet --version
```

#### 安装Python.NET
```bash
# PyPI（推荐）
pip3 install pythonnet

# 使用Conda（如果使用conda环境）
conda install -c conda-forge pythonnet

# 从源码编译（最新版本）
git clone https://github.com/pythonnet/pythonnet
cd pythonnet
pip3 install -e .
```

### 使用模式

#### 1. 从Python调用.NET代码
```python
import clr
import sys

# 添加.NET程序集引用
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

# 导入.NET命名空间
from System.Windows.Forms import Application, Form, Button, MessageBox
from System.Drawing import Point

# 创建Windows窗体
form = Form()
form.Text = "Hello from Python.NET"
form.Width = 300
form.Height = 200

button = Button()
button.Text = "Click Me!"
button.Location = Point(100, 50)

def button_click(sender, args):
    MessageBox.Show("Hello from Python!")

button.Click += button_click
form.Controls.Add(button)

Application.Run(form)
```

#### 2. 在.NET应用中嵌入Python
```csharp
using Python.Runtime;
using System;

class Program
{
    static void Main()
    {
        // 设置Python DLL路径（macOS）
        Runtime.PythonDLL = "/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/Python";
        // 或者使用pyenv安装的Python
        // Runtime.PythonDLL = "/Users/$USER/.pyenv/versions/3.11.0/Python.framework/Versions/3.11/Python";
        // 或者使用系统Python
        // Runtime.PythonDLL = "/System/Library/Frameworks/Python.framework/Versions/3.11/Python";
        
        PythonEngine.Initialize();
        
        using (Py.GIL())
        {
            // 导入并使用NumPy
            dynamic np = Py.Import("numpy");
            Console.WriteLine(np.cos(np.pi * 2));
            
            // 执行Python代码
            dynamic scope = Py.CreateScope();
            scope.Exec(@"
import sys
result = sys.version
            ");
            Console.WriteLine(scope.Get("result"));
            
            // 调用Python函数
            dynamic math = Py.Import("math");
            double result = math.sqrt(16);
            Console.WriteLine($"Square root of 16 is {result}");
        }
        
        PythonEngine.Shutdown();
    }
}
```

### 线程和GIL管理

```csharp
using Python.Runtime;
using System.Threading;
using System.Threading.Tasks;

class ThreadingExample
{
    static void Main()
    {
        // macOS Python路径配置
        Runtime.PythonDLL = "/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/Python";
        PythonEngine.Initialize();
        
        // 允许其他线程运行Python代码
        PythonEngine.BeginAllowThreads();
        
        // 创建多个使用Python的线程
        var tasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(() => RunPythonCode(threadId));
        }
        
        Task.WaitAll(tasks);
        
        PythonEngine.EndAllowThreads();
        PythonEngine.Shutdown();
    }
    
    static void RunPythonCode(int threadId)
    {
        // 每个线程必须获取GIL
        using (Py.GIL())
        {
            dynamic time = Py.Import("time");
            Console.WriteLine($"Thread {threadId} starting");
            time.sleep(1);
            Console.WriteLine($"Thread {threadId} finished");
        }
    }
}
```

### 性能优化

```python
import clr
import numpy as np
from System import Array, Double

# 高效的数据传输
def efficient_array_transfer():
    # 创建大型NumPy数组
    numpy_array = np.random.rand(1000000)
    
    # 使用内存指针高效转换为.NET数组
    net_array = Array[Double](numpy_array)
    
    # 在.NET中处理
    clr.AddReference("System.Linq")
    from System.Linq import Enumerable
    sum_value = Enumerable.Sum(net_array)
    
    return sum_value
```

### 高级特性

#### 事件处理
```python
import clr
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import Form, Button, Application

class EventExample:
    def __init__(self):
        self.form = Form()
        self.button = Button()
        self.button.Text = "Click me"
        
        # 订阅事件
        self.button.Click += self.on_button_click
        self.form.FormClosing += self.on_form_closing
        
        self.form.Controls.Add(self.button)
    
    def on_button_click(self, sender, args):
        print("Button clicked!")
    
    def on_form_closing(self, sender, args):
        print("Form is closing")
        # 可通过设置args.Cancel = True取消关闭
    
    def run(self):
        Application.Run(self.form)
```

#### 泛型支持
```python
import clr
from System.Collections.Generic import List, Dictionary

# 创建泛型列表
int_list = List[int]()
int_list.Add(1)
int_list.Add(2)
int_list.Add(3)

# 创建泛型字典
string_dict = Dictionary[str, int]()
string_dict["one"] = 1
string_dict["two"] = 2

# 使用LINQ处理泛型
clr.AddReference("System.Core")
from System.Linq import Enumerable

squared = Enumerable.Select(int_list, lambda x: x * x)
print(list(squared))  # [1, 4, 9]
```

### 故障排除

#### 初始化问题
```python
import os
import sys

# macOS Python路径配置
if sys.platform == "darwin":
    # Homebrew Python
    python_version = f"{sys.version_info.major}.{sys.version_info.minor}"
    homebrew_path = f"/usr/local/opt/python@{python_version}/Frameworks/Python.framework/Versions/{python_version}/Python"
    
    # pyenv Python
    pyenv_path = f"/Users/{os.getenv('USER')}/.pyenv/versions/{python_version}.0/Python.framework/Versions/{python_version}/Python"
    
    # 检查路径是否存在
    if os.path.exists(homebrew_path):
        os.environ['PYTHONNET_PYDLL'] = homebrew_path
    elif os.path.exists(pyenv_path):
        os.environ['PYTHONNET_PYDLL'] = pyenv_path
    else:
        # 使用系统Python作为后备
        os.environ['PYTHONNET_PYDLL'] = f"/System/Library/Frameworks/Python.framework/Versions/{python_version}/Python"
else:
    raise RuntimeError("此配置仅适用于macOS")

import clr
```

#### 程序集加载
```python
import clr
import sys

# 添加自定义程序集路径（macOS）
sys.path.append("/Users/$USER/MyAssemblies")
# 或者使用应用程序包内的路径
sys.path.append("/Applications/MyUnityApp.app/Contents/MacOS/Assemblies")

# 加载特定程序集
clr.AddReference("MyCustomAssembly")

# 处理程序集解析
def assembly_resolver(sender, args):
    # 自定义逻辑查找程序集
    assembly_path = find_assembly(args.Name)
    if assembly_path:
        return clr.LoadAssemblyFromFile(assembly_path)
    return None

# 注册自定义解析器
clr.AssemblyResolve += assembly_resolver
```

### 限制和注意事项

1. **性能开销**: 跨边界调用可能比原生代码慢400倍
2. **GIL限制**: 同一时间只有一个线程可以执行Python代码
3. **内存管理**: 注意跨边界的对象生命周期
4. **类型转换**: 某些类型需要手动转换
5. **调试挑战**: 混合语言调试可能很困难

### 实际应用项目
Wiki维护了使用Python.NET的项目列表，可作为实际实现模式的参考：
- humayoun007/pythonnet_sample
- NISystemsEngineering/rfmx-pythonnet
- pythonnet/demo目录中的官方演示

## Unity Plugin Demo

### 主要资源
- **PythonForUnity**: https://github.com/Maesla/PythonForUnity

### 项目概述
Unity的Python插件系统，展示了如何在Unity中集成Python脚本能力。

### 技术架构

#### 语言分布
- **C#**: 97.2%
- **Python**: 2.8%
- **许可证**: MIT

#### 核心组件
1. **外部工具**:
   - **Extenject**: 依赖注入框架（虽然核心系统相当解耦）
   - **Pythonnet**: 在.NET环境中运行Python的库

2. **配置要求**:
   - 在`Assets/StreamingAssets/Plugins/MicrokernelSystemSettings.json`中配置Python路径

### 实现特性

#### 1. Python脚本运行
- 在Unity中直接执行Python脚本
- 支持脚本的热重载

#### 2. Unity API访问
- 从Python脚本调用Unity API
- 访问场景对象和组件

#### 3. 项目集成
- 在Python脚本中使用项目特定类
- 与Unity的序列化系统集成

### 架构亮点

1. **微内核架构**
   - 插件系统基于微内核设计
   - 核心功能与扩展分离
   - 高度模块化

2. **依赖注入**
   - 使用Extenject管理依赖
   - 支持场景和组件配置
   - 便于测试和扩展

3. **解耦设计**
   - Python运行时与Unity系统解耦
   - 清晰的接口定义
   - 易于维护和升级

### 项目结构
```
PythonForUnity/
├── Assets/
│   ├── Scripts/           # C#脚本
│   ├── StreamingAssets/   # 配置文件
│   │   └── Plugins/
│   │       └── MicrokernelSystemSettings.json
│   └── Python/            # Python脚本
├── Packages/              # Unity包
└── ProjectSettings/       # Unity项目设置
```

### 配置示例
MicrokernelSystemSettings.json:
```json
{
    "pythonPath": "/usr/local/bin/python3",
    "pythonFrameworkPath": "/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/Python",
    "scriptPath": "Assets/Python/Scripts",
    "virtualEnvPath": "/Users/$USER/unity-python-env",
    "modules": [
        "numpy",
        "pandas",
        "strands-agents",
        "strands-agents-tools"
    ],
    "environmentVariables": {
        "PYTHONPATH": "Assets/Python/Scripts",
        "PYTHONHOME": "/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11"
    }
}
```

### 使用示例

#### C#端调用Python
```csharp
using PythonForUnity;

public class PythonRunner : MonoBehaviour
{
    private PythonEngine engine;
    
    void Start()
    {
        engine = new PythonEngine();
        engine.Initialize();
        
        // 执行Python脚本
        var result = engine.Execute(@"
import UnityEngine
obj = UnityEngine.GameObject('PythonCreated')
obj.transform.position = UnityEngine.Vector3(1, 2, 3)
        ");
    }
}
```

#### Python端访问Unity
```python
import UnityEngine

# 创建游戏对象
obj = UnityEngine.GameObject("MyObject")

# 添加组件
rigidbody = obj.AddComponent(UnityEngine.Rigidbody)
rigidbody.mass = 10.0

# 访问场景中的对象
player = UnityEngine.GameObject.Find("Player")
if player:
    transform = player.transform
    transform.position = UnityEngine.Vector3(0, 5, 0)
```

### 已知限制
1. **UI实现不完整**: 当前版本的UI集成尚未完成
2. **虚拟环境支持**: 暂不支持Python虚拟环境
3. **性能考虑**: Python脚本执行有一定开销
4. **平台限制**: 可能不支持所有Unity目标平台

### 改进建议
1. **完整UI支持**: 实现完整的Unity Editor UI集成
2. **虚拟环境**: 添加Python虚拟环境兼容性
3. **性能优化**: 优化Python-Unity通信性能
4. **文档完善**: 添加更多示例和最佳实践

### 开发状态
- **GitHub Stars**: 21
- **Forks**: 2
- **发布版本**: 无正式发布
- **活跃度**: 中等

## 部署和集成建议

### 推荐技术栈
1. **Strands Agent SDK** - 核心AI代理功能
2. **Python.NET** - C#与Python集成
3. **PythonForUnity架构** - Unity插件框架参考

### macOS开发流程

#### 第一阶段：环境搭建
1. **安装开发工具**
   ```bash
   # 安装Xcode Command Line Tools
   xcode-select --install
   
   # 安装Homebrew
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
   
   # 安装Python 3.11+
   brew install python@3.11
   
   # 安装.NET SDK
   brew install --cask dotnet
   ```

2. **配置Python环境**
   ```bash
   # 创建虚拟环境
   python3 -m venv ~/unity-python-env
   source ~/unity-python-env/bin/activate
   
   # 安装必要的包
   pip install --upgrade pip
   pip install pythonnet
   pip install strands-agents
   pip install strands-agents-tools
   ```

3. **设置Unity项目结构**
4. **集成依赖注入框架（Extenject）**

#### 第二阶段：基础集成
1. **实现Python运行时管理**
   ```csharp
   // macOS特定的Python初始化
   Runtime.PythonDLL = "/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/Python";
   PythonEngine.Initialize();
   ```

2. **建立C#-Python通信桥梁**
   - 配置Python路径检测
   - 实现安全的跨语言调用
   - 错误处理和异常管理

3. **创建基本的Unity Editor界面**
4. **测试简单的Python脚本执行**

#### 第三阶段：Agent集成
1. 集成Strands Agent SDK
2. 实现工具系统
3. 配置模型提供商
4. 开发聊天界面

#### 第四阶段：功能扩展
1. 添加Strands Agent Tools
2. 实现多代理支持
3. 集成MCP协议
4. 优化性能和用户体验

#### 第五阶段：打包发布
1. 创建Unity包
2. 编写文档
3. 准备示例项目
4. 发布到Unity Asset Store

### macOS最佳实践

1. **模块化设计**: 保持Python和C#代码分离
2. **异步处理**: 使用异步模式处理AI请求
3. **错误处理**: 完善的异常捕获和用户提示
4. **性能监控**: 集成性能分析工具
5. **安全考虑**: 沙箱化Python执行环境
6. **macOS特定优化**:
   ```bash
   # 设置正确的Python框架路径
   export PYTHONHOME=/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11
   export PYTHONPATH=/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/lib/python3.11/site-packages
   
   # 配置Unity Library路径
   export DYLD_LIBRARY_PATH=/usr/local/opt/python@3.11/Frameworks/Python.framework/Versions/3.11/lib:$DYLD_LIBRARY_PATH
   ```

### macOS注意事项

1. **许可证兼容性**: 确保所有依赖的许可证兼容
2. **macOS特定限制**:
   - 应用沙箱限制可能影响Python模块访问
   - 需要配置正确的代码签名
   - Gatekeeper可能阻止未签名的Python扩展
3. **版本管理**: 
   ```bash
   # 使用pyenv管理多个Python版本
   brew install pyenv
   pyenv install 3.11.0
   pyenv global 3.11.0
   ```
4. **资源管理**: 合理管理Python运行时资源
5. **性能优化**:
   - 在macOS上Python.NET性能可能比Windows稍低
   - 考虑使用Apple Silicon优化的Python版本
   - 避免频繁的GIL获取和释放

6. **构建和部署**:
   ```bash
   # Unity构建时包含Python运行时
   # 确保.app包中包含必要的Python框架
   # 配置Info.plist文件权限
   ```