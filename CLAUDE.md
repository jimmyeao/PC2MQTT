# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PC2MQTT is a Windows WPF application that publishes PC system metrics to MQTT for Home Assistant integration. It provides CPU/RAM monitoring and remote power control commands (shutdown, reboot, standby, hibernate) via MQTT.

**Target Framework:** .NET 8.0 Windows (WPF)

## Build and Development Commands

### Building
```bash
dotnet build PC2MQTT.csproj
dotnet build PC2MQTT.csproj --configuration Release
```

### Running
```bash
dotnet run --project PC2MQTT.csproj
```

### Publishing
```bash
dotnet publish PC2MQTT.csproj --configuration Release --output ./publish
```

## Architecture Overview

### Core Components

1. **MainWindow.xaml.cs** - Primary UI and application orchestrator
   - Initializes performance counters for CPU/RAM monitoring
   - Manages system tray (NotifyIcon) integration
   - Handles MQTT service initialization and lifecycle
   - Publishes Home Assistant MQTT auto-discovery configurations
   - Updates metrics on a 1-second timer (`_updateTimer`)

2. **MqttService.cs** - MQTT communication layer (Singleton)
   - Uses MQTTnet.Extensions.ManagedClient for auto-reconnect
   - Handles both TCP and WebSocket connections with optional TLS
   - Subscribes to power control command topics: `homeassistant/switch/{deviceId}/{command}/set`
   - Publishes to sensor state topics: `homeassistant/sensor/{deviceId}/{metric}/state`
   - Message queue mechanism for publishing before connection established

3. **AppSettings.cs** - Configuration management (Singleton)
   - Stores settings in `%LOCALAPPDATA%\PC2MQTT\settings.json`
   - Encrypts sensitive data (MQTT password) using CryptoHelper
   - Manages Windows startup registry entry when `RunAtWindowsBoot` is enabled
   - Settings loaded at app start and persisted on save

4. **api/pc_sensors.cs** - Power control actions
   - Executes system commands: `shutdown /s`, `shutdown /r`, `shutdown /h`
   - Optional confirmation dialogs when `UseSafeCommands` is enabled
   - Must run commands on UI thread using Dispatcher

5. **api/Sensors.cs** - Data models
   - `PCMetrics` singleton holds current CPU/RAM metrics
   - Updated by MainWindow timer and published to MQTT

### MQTT Topic Structure

**Sensors (Published by app):**
- Config: `homeassistant/sensor/{deviceId}/cpu_usage/config`
- State: `homeassistant/sensor/{deviceId}/cpu_usage/state`
- Metrics: `cpu_usage`, `total_ram`, `free_ram`, `used_ram`, `power_state`
- Power states: `on` (default), `off` (shutdown), `sleep` (standby), `hibernate`

**Switches (Subscribed by app):**
- Command: `homeassistant/switch/{deviceId}/{action}/set`
- State: `homeassistant/switch/{deviceId}/{action}/state`
- Actions: `shutdown`, `reboot`, `standby`, `hibernate`

### Key Dependencies

- **MQTTnet (5.0.1.1416)** - MQTT client library with custom reconnection logic
- **MaterialDesignThemes (5.3.0)** - UI theming
- **MaterialDesignColors (5.3.0)** - Material Design color palette
- **Hardcodet.NotifyIcon.Wpf (2.0.1)** - System tray icon
- **Serilog (4.3.0)** - Core logging framework
- **Serilog.Sinks.Console (6.0.0)** - Console logging sink
- **Serilog.Sinks.File (7.0.0)** - File logging sink to `%LOCALAPPDATA%\PC2MQTT\PC2MQTT_Log*.log`
- **Newtonsoft.Json (13.0.4)** - JSON serialization
- **Microsoft.Xaml.Behaviors.Wpf (1.1.135)** - WPF behaviors library

## Important Implementation Details

### Singleton Pattern
Both `MqttService` and `AppSettings` use thread-safe singleton pattern with double-check locking. Access via `.Instance` property after initialization.

### MqttService Initialization
Must call `MqttService.InitializeInstance(settings)` before accessing `MqttService.Instance`. The service requires explicit initialization with `await InitializeAsync(settings, clientId)`.

**MQTTnet 5.0 Changes:**
- Uses `MqttClient` directly instead of `ManagedMqttClient`
- Auto-reconnection implemented manually in `DisconnectedAsync` event with 5-second delay
- Payload accessed via `e.ApplicationMessage.Payload.ToArray()` (returns `ReadOnlySequence<byte>`)
- Requires `using System.Buffers;` for payload conversion
- Subscribe/Unsubscribe use builder pattern with options objects

### Performance Counters
CPU counter requires priming - first call to `NextValue()` returns 0. The app averages last 10 CPU readings in a queue for smoother values.

### Settings Encryption
`CryptoHelper.cs` encrypts passwords using Windows DPAPI (Data Protection API). Encrypted values stored in JSON, decrypted on access.

### UI Thread Requirements
Power command confirmation dialogs must run on UI thread using `System.Windows.Application.Current.Dispatcher.Invoke()`.

### Power State Tracking
The `power_state` sensor tracks PC power status. Before executing power commands, the app publishes the target state to MQTT with a 500ms delay to ensure message delivery. States: `on` (running), `off` (shutdown requested), `sleep` (standby requested), `hibernate` (hibernate requested). The `pc_sensors` class receives MqttService and deviceId references during initialization to publish state changes.

### Theme Management
Uses MaterialDesign themes. Theme applied at startup via `ApplyTheme()` and persisted in settings. Light/Dark mode toggle available.

## Common Development Patterns

### Adding a New Sensor
1. Add metric property to `PCMetrics` class in `api/Sensors.cs`
2. Update `PublishAutoDiscoveryConfigs()` in MainWindow to add config topic
3. Update `PublishMetrics()` to publish state value
4. Update performance counter collection in `OnTimedEvent()` if needed

### Adding a New Power Command
1. Add switch name to `PublishConfigurationsAsync()` entity list
2. Add command case to `HandleReceivedMessageAsync()` in MqttService
3. Create method in `pc_sensors.cs` to execute command
4. Add topic to `SubscribeToSwitchCommandsAsync()`

### MQTT Reconnection
When settings change, call `restartMqtt()` which:
- Unsubscribes from events
- Calls `ReinitializeAsync()`
- Re-establishes event handlers via `SetupMqttEventHandlers()`

## Logging

Logs written to `%LOCALAPPDATA%\PC2MQTT\PC2MQTT_Log*.log` (configured in LoggingConfig.cs). Use Serilog methods: `Log.Debug()`, `Log.Information()`, `Log.Error()`.

## Application Settings Location

All persistent data stored in: `%LOCALAPPDATA%\PC2MQTT\`
- `settings.json` - User configuration
- `PC2MQTT_Log*.log` - Application logs
