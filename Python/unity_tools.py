"""
Unity开发工具配置模块
管理Unity Agent的预定义工具和MCP工具集成
"""

import sys
import os
import logging
import asyncio
from typing import List, Dict, Any, Optional

logger = logging.getLogger(__name__)

# 全局工具可用性标识
TOOLS_AVAILABLE = False
MCP_AVAILABLE = False

# 工具模块引用
file_read_module = None
file_write_module = None
editor_module = None
python_repl_module = None
calculator_module = None
memory_module = None
current_time_module = None
shell_module = None
http_request_module = None

# 新增工具模块引用
environment_module = None
use_browser_module = None
use_aws_module = None
retrieve_module = None
generate_image_module = None
mem0_memory_module = None
think_module = None
image_reader_module = None
sleep_module = None
cron_module = None
journal_module = None
workflow_module = None
batch_module = None
swarm_module = None
agent_graph_module = None


class UnityToolsManager:
    """Unity开发工具管理器"""
    
    def __init__(self):
        self.tools_available = False
        self.mcp_available = False
        self.tool_modules = {}
        self.mcp_tools = []
        self._initialize_tools()
    
    def _initialize_tools(self):
        """初始化所有工具"""
        self._load_strands_tools()
        self._load_mcp_support()
    
    def _load_strands_tools(self):
        """加载Strands预定义工具"""
        global TOOLS_AVAILABLE, file_read_module, file_write_module, editor_module
        global python_repl_module, calculator_module, memory_module, current_time_module
        global shell_module, http_request_module
        # 新增工具全局引用
        global environment_module, use_browser_module, use_aws_module, retrieve_module
        global generate_image_module, mem0_memory_module, think_module, image_reader_module
        global sleep_module, cron_module, journal_module, workflow_module
        global batch_module, swarm_module, agent_graph_module
        
        try:
            # 从Unity PathManager获取strands tools路径
            # 注意：这里需要通过Unity C#接口获取路径配置
            # 暂时使用环境变量或配置文件作为后备方案
            strands_tools_path = os.environ.get('STRANDS_TOOLS_PATH', "/Users/caobao/projects/strands/tools/src")
            if strands_tools_path and strands_tools_path not in sys.path:
                sys.path.insert(0, strands_tools_path)
            
            print(f"[Debug] 正在从路径加载Strands工具: {strands_tools_path}")
            print(f"[Debug] Python路径: {sys.path[:3]}...")  # 只显示前3个路径
            
            # 导入核心工具模块
            import strands_tools.file_read as file_read_module
            import strands_tools.file_write as file_write_module  
            import strands_tools.editor as editor_module
            import strands_tools.python_repl as python_repl_module
            import strands_tools.calculator as calculator_module
            import strands_tools.memory as memory_module
            import strands_tools.current_time as current_time_module
            import strands_tools.shell as shell_module
            import strands_tools.http_request as http_request_module
            
            # 导入新增工具模块 - 使用安全导入处理可选依赖
            import strands_tools.environment as environment_module
            import strands_tools.use_aws as use_aws_module
            import strands_tools.retrieve as retrieve_module
            import strands_tools.generate_image as generate_image_module
            import strands_tools.think as think_module
            import strands_tools.image_reader as image_reader_module
            import strands_tools.sleep as sleep_module
            import strands_tools.cron as cron_module
            import strands_tools.journal as journal_module
            import strands_tools.workflow as workflow_module
            import strands_tools.batch as batch_module
            import strands_tools.swarm as swarm_module
            
            # 可选依赖工具 - 如果导入失败则跳过
            use_browser_module = None
            mem0_memory_module = None
            
            try:
                import strands_tools.use_browser as use_browser_module
                logger.info("✓ use_browser工具可用")
            except ImportError as e:
                logger.info(f"use_browser工具不可用 (缺少playwright): {e}")
                use_browser_module = None
            
            try:
                import strands_tools.mem0_memory as mem0_memory_module  
                logger.info("✓ mem0_memory工具可用")
            except ImportError as e:
                logger.info(f"mem0_memory工具不可用 (缺少mem0ai): {e}")
                mem0_memory_module = None
            import strands_tools.agent_graph as agent_graph_module
            
            # 存储所有工具模块引用 - 过滤掉None值
            tool_modules = {
                # 核心工具
                'file_read': file_read_module,
                'file_write': file_write_module,
                'editor': editor_module,
                'python_repl': python_repl_module,
                'calculator': calculator_module,
                'memory': memory_module,
                'current_time': current_time_module,
                'shell': shell_module,
                'http_request': http_request_module,
                
                # 新增工具
                'environment': environment_module,
                'use_aws': use_aws_module,
                'retrieve': retrieve_module,
                'generate_image': generate_image_module,
                'think': think_module,
                'image_reader': image_reader_module,
                'sleep': sleep_module,
                'cron': cron_module,
                'journal': journal_module,
                'workflow': workflow_module,
                'batch': batch_module,
                'swarm': swarm_module,
                'agent_graph': agent_graph_module
            }
            
            # 添加可选工具（如果可用）
            if use_browser_module is not None:
                tool_modules['use_browser'] = use_browser_module
            if mem0_memory_module is not None:
                tool_modules['mem0_memory'] = mem0_memory_module
            
            # 过滤掉None值并存储
            self.tool_modules = {k: v for k, v in tool_modules.items() if v is not None}
            
            print(f"[Python] Strands预定义工具导入成功，总共{len(self.tool_modules)}个工具")
            print(f"[Python] 已导入的工具: {list(self.tool_modules.keys())}")
            TOOLS_AVAILABLE = True
            self.tools_available = True
            
        except ImportError as e:
            print(f"[Python] Strands工具导入失败: {e}")
            print("[Python] 将使用无工具模式")
            TOOLS_AVAILABLE = False
            self.tools_available = False
    
    def _load_mcp_support(self):
        """加载MCP支持"""
        global MCP_AVAILABLE
        
        try:
            from mcp import StdioServerParameters, stdio_client
            from strands.tools.mcp import MCPClient as StrandsMCPClient
            import asyncio
            import subprocess
            import threading
            from datetime import timedelta
            from concurrent.futures import Future, ThreadPoolExecutor
            import weakref
            
            MCP_AVAILABLE = True
            self.mcp_available = True
            print("[Python] MCP支持加载成功")
            
        except ImportError as e:
            print(f"[Python] MCP支持加载失败: {e}")
            MCP_AVAILABLE = False
            self.mcp_available = False
    
    def get_unity_tools(self, include_mcp=True, agent_instance=None):
        """获取适合Unity开发的工具集合"""
        if not self.tools_available:
            logger.warning("Strands工具不可用，返回空工具列表")
            return []
        
        unity_tools = []
        
        # 检查操作系统兼容性
        import platform
        is_windows = platform.system() == 'Windows'
        
        # 核心工具组
        core_tools = [
            ('file_read', '文件读取 - 读取配置文件、代码文件、数据集'),
            ('file_write', '文件写入 - 写入结果到文件、创建新文件'),
            ('environment', '环境管理 - 管理环境变量、配置管理'),
            ('http_request', 'HTTP请求 - 进行API调用、获取网络数据'),
            ('use_browser', '浏览器自动化 - 网页抓取、自动化测试、表单填写'),
            ('calculator', '数学计算 - 执行数学运算、符号数学、方程求解')
        ]
        
        # 添加非Windows平台专用工具
        if not is_windows:
            core_tools.extend([
                ('shell', 'Shell执行 - 执行shell命令、与操作系统交互'),
                ('python_repl', 'Python执行 - 运行Python代码片段、数据分析')
            ])
        
        # AWS和云服务工具组
        aws_tools = [
            ('use_aws', 'AWS服务 - 与AWS服务交互、云资源管理'),
            ('retrieve', '知识检索 - 从Amazon Bedrock Knowledge Bases检索信息'),
            ('memory', '文档管理 - 在Amazon Bedrock Knowledge Bases中存储、检索文档'),
            ('generate_image', '图像生成 - 为各种应用创建AI生成的图像')
        ]
        
        # AI和智能工具组
        ai_tools = [
            ('mem0_memory', '记忆管理 - 跨代理运行存储用户和代理记忆'),
            ('think', '高级推理 - 高级推理、多步骤思考过程')
        ]
        
        # 媒体处理工具组
        media_tools = [
            ('image_reader', '图像读取 - 处理和读取图像文件进行AI分析')
        ]
        
        # 时间和任务管理工具组
        time_tools = [
            ('current_time', '时间获取 - 获取指定时区的当前时间'),
            ('sleep', '延时控制 - 暂停执行指定秒数')
        ]
        
        # 添加非Windows平台专用任务调度工具
        if not is_windows:
            time_tools.append(('cron', '任务调度 - 使用cron语法调度和管理重复任务'))
        
        # 文档和日志工具组
        doc_tools = [
            ('journal', '日志管理 - 创建结构化日志、维护文档')
        ]
        
        # 工作流和协调工具组
        workflow_tools = [
            ('workflow', '工作流管理 - 定义、执行和管理多步骤自动化工作流'),
            ('batch', '批量处理 - 并行调用多个其他工具')
        ]
        
        # 多代理工具组
        multi_agent_tools = [
            ('swarm', '集群智能 - 协调多个AI代理通过集体智能解决复杂问题'),
            ('agent_graph', '代理图谱 - 为复杂多代理系统创建和可视化代理关系图')
        ]
        
        # 添加所有工具组
        all_tool_groups = [
            ('核心工具', core_tools),
            ('AWS和云服务', aws_tools),
            ('AI和智能', ai_tools),
            ('媒体处理', media_tools),
            ('时间和任务管理', time_tools),
            ('文档和日志', doc_tools),
            ('工作流和协调', workflow_tools),
            ('多代理系统', multi_agent_tools)
        ]
        
        # 逐组添加工具
        for group_name, tools in all_tool_groups:
            group_tools = []
            for tool_name, description in tools:
                try:
                    if tool_name in self.tool_modules:
                        unity_tools.append(self.tool_modules[tool_name])
                        group_tools.append(tool_name)
                except KeyError:
                    logger.warning(f"{tool_name}工具不可用")
            
            if group_tools:
                logger.info(f"✓ 添加{group_name}组: {', '.join(group_tools)}")
                print(f"[Debug] 添加{group_name}组: {', '.join(group_tools)}")
            else:
                logger.warning(f"⚠️ {group_name}组中没有可用工具")
                print(f"[Debug] ⚠️ {group_name}组中没有可用工具")
        
        # MCP工具 - 外部工具和服务集成
        if include_mcp and self.mcp_available and agent_instance:
            try:
                # 如果提供了agent实例，使用其MCP加载方法
                if hasattr(agent_instance, '_load_mcp_tools'):
                    mcp_tools = agent_instance._load_mcp_tools()
                    if mcp_tools:
                        unity_tools.extend(mcp_tools)
                        logger.info(f"✓ 添加MCP工具: {len(mcp_tools)} 个工具")
                        # 存储MCP工具引用
                        self.mcp_tools = mcp_tools
                else:
                    logger.warning("Agent实例不支持MCP工具加载")
            except Exception as e:
                logger.warning(f"MCP工具加载失败: {e}")
        else:
            if include_mcp and self.mcp_available:
                logger.info("ℹ️ MCP工具需要agent实例，跳过MCP工具加载")
            else:
                logger.info("ℹ️ MCP支持不可用，跳过MCP工具加载")
        
        if unity_tools:
            tool_names = [tool.__name__ if hasattr(tool, '__name__') else str(tool) for tool in unity_tools]
            logger.info(f"🎉 成功配置 {len(unity_tools)} 个Unity开发工具")
            logger.info(f"可用工具列表: {tool_names}")
            print(f"[Debug] 🎉 最终配置了 {len(unity_tools)} 个工具")
            print(f"[Debug] 工具列表: {tool_names}")
        else:
            logger.warning("⚠️ 没有可用的Unity开发工具")
            print("[Debug] ⚠️ 没有可用的Unity开发工具")
        
        return unity_tools
    
    def _load_mcp_tools(self):
        """加载MCP工具"""
        if not self.mcp_available:
            logger.warning("MCP支持不可用")
            return []
        
        # 由于MCP工具加载逻辑复杂且依赖agent_core中的其他方法，
        # 这里返回空列表，实际的MCP工具加载仍在agent_core中处理
        logger.info("MCP工具加载将在agent_core中处理")
        return []
    
    def get_available_tool_names(self):
        """获取可用工具名称列表"""
        if not self.tools_available:
            return []
        
        # 检查操作系统兼容性
        import platform
        is_windows = platform.system() == 'Windows'
        
        base_tools = [
            # 核心工具（跨平台）
            "file_read", "file_write", "environment", "http_request", 
            "use_browser", "calculator",
            # AWS和云服务
            "use_aws", "retrieve", "memory", "generate_image",
            # AI和智能
            "mem0_memory", "think",
            # 媒体处理
            "image_reader",
            # 时间和任务管理（跨平台）
            "current_time", "sleep",
            # 文档和日志
            "journal",
            # 工作流和协调
            "workflow", "batch",
            # 多代理系统
            "swarm", "agent_graph"
        ]
        
        # 添加非Windows平台专用工具
        if not is_windows:
            base_tools.extend([
                "shell", "python_repl", "cron"
            ])
        
        if self.mcp_available and self.mcp_tools:
            # 添加MCP工具名称
            mcp_names = [tool.name if hasattr(tool, 'name') else str(tool) for tool in self.mcp_tools]
            base_tools.extend(mcp_names)
        
        return base_tools
    
    def is_tools_available(self):
        """检查工具是否可用"""
        return self.tools_available
    
    def is_mcp_available(self):
        """检查MCP是否可用"""
        return self.mcp_available


# 全局工具管理器实例
_unity_tools_manager = None

def get_unity_tools_manager():
    """获取全局Unity工具管理器实例"""
    global _unity_tools_manager
    if _unity_tools_manager is None:
        _unity_tools_manager = UnityToolsManager()
    return _unity_tools_manager


# 便捷函数
def get_unity_tools(include_mcp=True, agent_instance=None):
    """获取Unity开发工具集合的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.get_unity_tools(include_mcp, agent_instance)


def get_available_tool_names():
    """获取可用工具名称列表的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.get_available_tool_names()


def is_tools_available():
    """检查工具是否可用的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.is_tools_available()


def is_mcp_available():
    """检查MCP是否可用的便捷函数"""
    manager = get_unity_tools_manager()
    return manager.is_mcp_available()


# 向后兼容性：导出全局变量
def update_global_availability():
    """更新全局可用性变量"""
    global TOOLS_AVAILABLE, MCP_AVAILABLE
    manager = get_unity_tools_manager()
    TOOLS_AVAILABLE = manager.is_tools_available()
    MCP_AVAILABLE = manager.is_mcp_available()


# 初始化时更新全局变量
update_global_availability()