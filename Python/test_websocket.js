#!/usr/bin/env node

const WebSocket = require('ws');

console.log('Testing WebSocket connection to Unity MCP server...');

const ws = new WebSocket('ws://localhost:8090/McpUnity', {
    headers: {
        'X-Client-Name': 'Test MCP Client'
    },
    origin: 'Test MCP Client'
});

let connected = false;

ws.on('open', function open() {
    console.log('✓ WebSocket connected successfully');
    connected = true;
    
    // Send a simple test message
    const testMessage = {
        id: '1',
        method: 'unity://hierarchy',
        params: {}
    };
    
    console.log('Sending test message:', JSON.stringify(testMessage));
    ws.send(JSON.stringify(testMessage));
});

ws.on('message', function message(data) {
    console.log('✓ Received message:', data.toString());
    ws.close();
});

ws.on('error', function error(err) {
    console.log('✗ WebSocket error:', err.message);
});

ws.on('close', function close(code, reason) {
    console.log(`✓ WebSocket closed with code: ${code}, reason: ${reason}`);
    if (!connected) {
        console.log('✗ Connection was never established');
    }
});

// Timeout after 10 seconds
setTimeout(() => {
    if (ws.readyState === WebSocket.OPEN) {
        console.log('⚠️ Timeout reached, closing connection');
        ws.close();
    } else if (!connected) {
        console.log('✗ Connection timed out');
        process.exit(1);
    }
}, 10000);