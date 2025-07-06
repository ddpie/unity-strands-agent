"""
Unity SSL配置模块
处理Unity环境下的SSL证书配置，支持多种证书路径和降级策略
"""

import os
import sys
import ssl


class UnitySSLConfig:
    """Unity环境SSL证书配置管理器"""
    
    # 系统Python的certifi证书路径
    SYSTEM_CERTIFI_PATHS = [
        # 最新版本优先
        '/usr/local/lib/python3.13/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.12/site-packages/certifi/cacert.pem', 
        '/usr/local/lib/python3.11/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.10/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.9/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.8/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.7/site-packages/certifi/cacert.pem',
        '/usr/local/lib/python3.6/site-packages/certifi/cacert.pem',
        # macOS Framework路径
        '/Library/Frameworks/Python.framework/Versions/3.13/lib/python3.13/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.12/lib/python3.12/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.11/lib/python3.11/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.10/lib/python3.10/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.9/lib/python3.9/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.8/lib/python3.8/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.7/lib/python3.7/site-packages/certifi/cacert.pem',
        '/Library/Frameworks/Python.framework/Versions/3.6/lib/python3.6/site-packages/certifi/cacert.pem',   
    ]
    
    # macOS系统证书路径
    MACOS_CERT_PATHS = [
        '/etc/ssl/cert.pem',  # 标准位置
        '/usr/local/etc/openssl/cert.pem',  # Homebrew OpenSSL
        '/opt/homebrew/etc/openssl/cert.pem',  # Apple Silicon Homebrew
        '/System/Library/OpenSSL/certs/cert.pem',  # 系统OpenSSL
    ]
    
    def __init__(self):
        self.ssl_configured = False
        self.cert_path = None
    
    def configure(self):
        """配置SSL证书，返回是否成功"""
        # 1. 尝试使用certifi模块
        if self._try_certifi():
            return True
            
        # 2. 尝试系统Python的certifi路径
        if self._try_system_certifi():
            return True
            
        # 3. 尝试macOS系统证书路径
        if self._try_macos_certs():
            return True
            
        # 4. 都失败了，配置为禁用SSL验证
        self._disable_ssl_verification()
        return False
    
    def _try_certifi(self):
        """尝试使用certifi模块的证书"""
        try:
            import certifi
            cert_path = certifi.where()
            
            if os.path.exists(cert_path):
                self._set_cert_path(cert_path)
                print(f"[Python] ✓ 使用certifi证书路径: {cert_path}")
                return True
            else:
                print(f"[Python] ⚠️ certifi证书文件不存在: {cert_path}")
                
        except ImportError as e:
            print(f"[Python] ⚠️ certifi不可用: {e}")
        
        return False
    
    def _try_system_certifi(self):
        """尝试系统Python的certifi路径"""
        # 首先尝试从环境变量获取配置的路径
        ssl_cert_file = os.environ.get('SSL_CERT_FILE_PATH')
        if ssl_cert_file and os.path.exists(ssl_cert_file):
            self._set_cert_path(ssl_cert_file)
            print(f"[Python] ✓ 使用配置的SSL证书路径: {ssl_cert_file}")
            return True
        
        # 然后尝试预定义的系统路径
        for cert_path in self.SYSTEM_CERTIFI_PATHS:
            if os.path.exists(cert_path):
                self._set_cert_path(cert_path)
                print(f"[Python] ✓ 使用系统Python证书路径: {cert_path}")
                return True
        return False
    
    def _try_macos_certs(self):
        """尝试macOS系统证书路径"""
        # 首先尝试从环境变量获取配置的证书目录
        ssl_cert_dir = os.environ.get('SSL_CERT_DIR_PATH')
        if ssl_cert_dir and os.path.exists(ssl_cert_dir):
            # 在配置的目录中查找证书文件
            for cert_file in ['cert.pem', 'ca-certificates.crt', 'cacert.pem']:
                cert_path = os.path.join(ssl_cert_dir, cert_file)
                if os.path.exists(cert_path):
                    self._set_cert_path(cert_path)
                    print(f"[Python] ✓ 使用配置的证书目录中的证书: {cert_path}")
                    return True
        
        # 然后尝试预定义的macOS路径
        for cert_path in self.MACOS_CERT_PATHS:
            if os.path.exists(cert_path):
                self._set_cert_path(cert_path)
                print(f"[Python] ✓ 使用系统证书路径: {cert_path}")
                return True
        return False
    
    def _set_cert_path(self, cert_path):
        """设置证书路径到环境变量"""
        self.cert_path = cert_path
        self.ssl_configured = True
        os.environ['SSL_CERT_FILE'] = cert_path
        os.environ['REQUESTS_CA_BUNDLE'] = cert_path
        os.environ['CURL_CA_BUNDLE'] = cert_path
    
    def _disable_ssl_verification(self):
        """禁用SSL验证（仅用于开发环境）"""
        print("[Python] ⚠️ 未找到有效的SSL证书，将禁用SSL验证")
        
        try:
            # 禁用urllib3的SSL警告
            import urllib3
            urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
        except ImportError:
            pass
        
        # 设置环境变量禁用SSL验证
        os.environ['PYTHONHTTPSVERIFY'] = '0'
        os.environ['CURL_CA_BUNDLE'] = ''
        os.environ['REQUESTS_CA_BUNDLE'] = ''
        
        print("[Python] ⚠️ SSL验证已禁用 - 仅用于开发环境")
    
    def configure_aws_ssl(self):
        """为AWS SDK配置SSL设置"""
        try:
            import boto3
            import botocore.config
            if not self.ssl_configured:
                print("[Python] 为AWS Bedrock配置SSL设置")
        except ImportError:
            pass
    
    def get_status(self):
        """获取SSL配置状态"""
        return {
            'configured': self.ssl_configured,
            'cert_path': self.cert_path,
            'ssl_verify_enabled': self.ssl_configured
        }


# 便捷函数
def configure_ssl_for_unity():
    """为Unity环境配置SSL证书的便捷函数"""
    config = UnitySSLConfig()
    return config.configure()


# 全局实例
_ssl_config = None

def get_ssl_config():
    """获取全局SSL配置实例"""
    global _ssl_config
    if _ssl_config is None:
        _ssl_config = UnitySSLConfig()
    return _ssl_config