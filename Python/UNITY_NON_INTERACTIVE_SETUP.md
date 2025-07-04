# Unity非交互式工具配置

## 概述

为了解决Unity AI Agent中工具需要用户交互（如输入'y'确认）而导致卡住的问题，我们实施了以下改进：

## 主要改动

### 1. 创建非交互式工具管理器
**文件**: `unity_non_interactive_tools.py`
- 自动设置 `BYPASS_TOOL_CONSENT=true` 环境变量
- 提供工具包装功能，确保所有工具以非交互模式运行
- 强制设置 `non_interactive_mode=True` 参数

### 2. Unity专用Shell工具
**文件**: `unity_shell_tool.py`
- 完全无需用户确认的Shell工具
- 使用 `subprocess` 直接执行命令
- 支持超时控制（默认30秒）
- 自动处理命令输出和错误

### 3. 增强工具调用跟踪
**文件**: `agent_core.py` 和 `tool_tracker.py`
- 添加详细的工具调用进度指示器
- 显示工具名称、输入参数、执行状态
- 定期提供执行进度更新（每15秒）
- 在工具完成时显示结果

### 4. 修改Agent初始化
**文件**: `agent_core.py`
- 在Agent初始化时自动设置非交互模式
- 包装所有工具以确保非交互执行
- 优先使用Unity专用的shell工具

## 受影响的Strands工具

以下原本需要用户交互的工具现在将自动执行：

1. **shell** - 执行命令前的确认
2. **file_write** - 写入文件前的确认
3. **editor** - 编辑文件前的确认
4. **python_repl** - 执行Python代码前的确认

## 环境变量设置

系统会自动设置以下环境变量：

```python
os.environ["BYPASS_TOOL_CONSENT"] = "true"
os.environ["PYTHON_REPL_INTERACTIVE"] = "false"
os.environ["SHELL_DEFAULT_TIMEOUT"] = "60"
```

## 测试方法

### 1. 检查工具调用输出
在Unity AI Assistant中输入一个需要工具调用的请求，例如：
```
请帮我查看当前目录的文件列表
```

**期望结果**：
- 看到 "🔧 **工具追踪系统已启用**" 消息
- 看到具体的工具调用过程
- 不会出现等待用户输入的提示
- 工具应该自动执行并返回结果

### 2. 检查日志输出
查看Unity Console或日志文件，应该看到：
```
已启用Unity非交互模式 - 所有工具将自动执行
✓ 添加Shell工具: unity_shell（Unity专用版本，无需用户确认）
```

### 3. 验证Shell命令执行
输入需要执行命令的请求：
```
请执行 ls -la 命令查看文件详情
```

**期望结果**：
- 命令立即执行，不等待确认
- 返回命令输出结果
- 在聊天中看到执行过程

## 故障排除

### 如果工具仍然卡住
1. 检查日志中是否有"已启用Unity非交互模式"消息
2. 确认环境变量是否正确设置
3. 查看是否有错误日志

### 如果Unity专用工具不可用
系统会自动回退到标准工具，但可能仍有交互问题。检查日志中的警告信息。

### 手动设置环境变量
如果自动设置失败，可以在启动前手动设置：
```python
import os
os.environ["BYPASS_TOOL_CONSENT"] = "true"
```

## 注意事项

1. **安全性**: 非交互模式意味着工具会自动执行，请确保在安全环境中使用
2. **监控**: 建议监控工具执行结果，确保没有意外的操作
3. **回退机制**: 如果Unity专用工具失败，系统会回退到标准工具

## 版本信息

- 修改日期: 2025-01-04
- 适用版本: Unity AI Agent v1.0+
- Python要求: Python 3.7+