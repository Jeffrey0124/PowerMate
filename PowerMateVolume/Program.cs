using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using PowerMate;

namespace PowerMateVolume;

internal static class Program
{
    private const string AppName = "PowerMateVolume";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        AppOptions options;
        try
        {
            options = LoadOptions(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[{AppName}] Configuration error: {exception.Message}");
            return 2;
        }

        using var log = new Logger(options.LogLevel);
        using var volume = new SystemVolume(log);
        using var cancellation = new CancellationTokenSource();
        using var source = PowerMateInputSource.Create(options, log);

        log.Info(options.Probe
            ? "Probe mode: listening for PowerMate events without changing volume."
            : $"Listening for PowerMate events. Volume step: {options.VolumeStep:P0}.");

        source.InputReceived += (_, input) => HandleInput(input, options, volume, log);
        source.Start(cancellation.Token);

        using var exit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            exit.Set();
        };

        exit.Wait();
        return 0;
    }

    private static void HandleInput(KnobInput input, AppOptions options, SystemVolume volume, Logger log)
    {
        if (input.RotationDistance > 0 && input.Direction == KnobDirection.Clockwise)
        {
            log.Info($"clockwise {input.RotationDistance}");
            if (!options.Probe)
            {
                volume.Change(options.VolumeStep * input.RotationDistance);
            }
        }
        else if (input.RotationDistance > 0 && input.Direction == KnobDirection.Counterclockwise)
        {
            log.Info($"counterclockwise {input.RotationDistance}");
            if (!options.Probe)
            {
                volume.Change(-options.VolumeStep * input.RotationDistance);
            }
        }
        else if (input.IsPressed)
        {
            log.Info("pressed");
            if (!options.Probe && options.StartMutedToggleOnPress)
            {
                volume.ToggleMute();
            }
        }
        else
        {
            log.Info("released");
        }
    }

    private static AppOptions LoadOptions(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "PowerMateVolume.json");
        var options = File.Exists(configPath)
            ? JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(configPath), JsonOptions) ?? new AppOptions()
            : new AppOptions();

        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, JsonSerializer.Serialize(options, JsonOptions));
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--probe", StringComparison.OrdinalIgnoreCase))
            {
                options.Probe = true;
            }
            else if (arg.Equals("--volume-step", StringComparison.OrdinalIgnoreCase))
            {
                options.VolumeStep = ParseRequiredDouble(args, ++i, "--volume-step");
            }
            else if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase))
            {
                options.Backend = ParseRequiredString(args, ++i, "--backend");
            }
            else if (arg.StartsWith("--volume-step=", StringComparison.OrdinalIgnoreCase))
            {
                options.VolumeStep = ParseDouble(arg["--volume-step=".Length..], "--volume-step");
            }
            else if (arg.StartsWith("--backend=", StringComparison.OrdinalIgnoreCase))
            {
                options.Backend = arg["--backend=".Length..];
            }
            else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                Environment.Exit(0);
            }
            else if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyStep))
            {
                options.VolumeStep = legacyStep;
            }
            else
            {
                throw new ArgumentException($"Unknown argument '{arg}'. Use --help for usage.");
            }
        }

        if (options.VolumeStep <= 0 || options.VolumeStep > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.VolumeStep), "volumeStep must be greater than 0 and less than or equal to 1.");
        }

        return options;
    }

    private static string ParseRequiredString(string[] args, int index, string optionName)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        return args[index];
    }

    private static double ParseRequiredDouble(string[] args, int index, string optionName)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        return ParseDouble(args[index], optionName);
    }

    private static double ParseDouble(string value, string optionName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{optionName} must be a number such as 0.01.");
        }

        return result;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PowerMateVolume");
        Console.WriteLine("  PowerMateVolume.exe");
        Console.WriteLine("  PowerMateVolume.exe --probe");
        Console.WriteLine("  PowerMateVolume.exe --volume-step 0.02");
        Console.WriteLine("  PowerMateVolume.exe --backend auto|winusb|hid");
    }
}

internal sealed class AppOptions
{
    public double VolumeStep { get; set; } = 0.01;
    public bool StartMutedToggleOnPress { get; set; } = true;
    public string LogLevel { get; set; } = "Info";
    public string Backend { get; set; } = "auto";
    public bool Probe { get; set; }
}

internal abstract class PowerMateInputSource : IDisposable
{
    public event EventHandler<KnobInput>? InputReceived;

    public abstract void Start(CancellationToken cancellationToken);
    public abstract void Dispose();

    protected void OnInputReceived(KnobInput input) => InputReceived?.Invoke(this, input);

    public static PowerMateInputSource Create(AppOptions options, Logger log)
    {
        var backend = options.Backend.Trim().ToLowerInvariant();
        if (backend == "hid")
        {
            return new HidPowerMateInputSource(log);
        }

        if (backend == "winusb")
        {
            return new WinUsbPowerMateInputSource(log);
        }

        if (backend != "auto")
        {
            throw new ArgumentException("backend must be auto, winusb, or hid.");
        }

        var winUsbSource = new WinUsbPowerMateInputSource(log);
        if (winUsbSource.CanOpenNow())
        {
            return winUsbSource;
        }

        winUsbSource.Dispose();
        return new HidPowerMateInputSource(log);
    }
}

internal sealed class HidPowerMateInputSource : PowerMateInputSource
{
    private readonly Logger _log;
    private readonly IPowerMateClient _client;

    public HidPowerMateInputSource(Logger log)
    {
        _log = log;
        _client = new PowerMateClient();
    }

    public override void Start(CancellationToken cancellationToken)
    {
        _log.Info(_client.IsConnected
            ? "Using HID backend. PowerMate is connected."
            : "Using HID backend. Waiting for a PowerMate to be connected.");

        _client.IsConnectedChanged += (_, isConnected) =>
        {
            _log.Info(isConnected
                ? "PowerMate connected."
                : "PowerMate disconnected; waiting for reconnect.");
        };

        try
        {
            _client.LightBrightness = 0;
        }
        catch (Exception exception)
        {
            _log.Warn($"Could not set PowerMate light brightness: {exception.Message}");
        }

        _client.InputReceived += (_, input) =>
        {
            _log.Debug($"HID raw event: {input}");
            OnInputReceived(new KnobInput(
                input.IsPressed,
                input.RotationDirection switch
                {
                    RotationDirection.Clockwise => KnobDirection.Clockwise,
                    RotationDirection.Counterclockwise => KnobDirection.Counterclockwise,
                    _ => KnobDirection.None
                },
                input.RotationDistance));
        };
    }

    public override void Dispose() => _client.Dispose();
}

internal sealed class WinUsbPowerMateInputSource : PowerMateInputSource
{
    private static readonly Guid DeviceInterfaceGuid = new("6BA23D9A-A3AE-4349-8DE0-5E91AAAF659D");

    private readonly Logger _log;
    private Thread? _readerThread;

    public WinUsbPowerMateInputSource(Logger log)
    {
        _log = log;
    }

    public bool CanOpenNow()
    {
        return WinUsbNative.EnumerateDevicePaths(DeviceInterfaceGuid).Any();
    }

    public override void Start(CancellationToken cancellationToken)
    {
        _log.Info("Using WinUSB/libusb backend.");
        _readerThread = new Thread(() => ReadLoop(cancellationToken))
        {
            IsBackground = true,
            Name = "PowerMate WinUSB reader"
        };
        _readerThread.Start();
    }

    private void ReadLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var devicePath = WinUsbNative.EnumerateDevicePaths(DeviceInterfaceGuid).FirstOrDefault();
                if (devicePath is null)
                {
                    _log.Info("Waiting for a WinUSB PowerMate to be connected.");
                    Sleep(cancellationToken, TimeSpan.FromSeconds(2));
                    continue;
                }

                _log.Info("WinUSB PowerMate connected.");
                using var handle = WinUsbNative.OpenDevice(devicePath);
                if (handle.IsInvalid)
                {
                    _log.Warn($"Could not open WinUSB device path; Windows error {Marshal.GetLastWin32Error()}.");
                    Sleep(cancellationToken, TimeSpan.FromSeconds(2));
                    continue;
                }

                if (!WinUsbNative.WinUsb_Initialize(handle, out var interfaceHandle))
                {
                    _log.Warn($"WinUsb_Initialize failed; Windows error {Marshal.GetLastWin32Error()}.");
                    Sleep(cancellationToken, TimeSpan.FromSeconds(2));
                    continue;
                }

                using var winUsb = new WinUsbNative.SafeWinUsbHandle(interfaceHandle);
                var buffer = new byte[6];

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!WinUsbNative.WinUsb_ReadPipe(winUsb.DangerousGetHandle(), 0x81, buffer, buffer.Length, out var bytesRead, IntPtr.Zero))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == WinUsbNative.ErrorSemTimeout)
                        {
                            continue;
                        }

                        _log.Warn($"WinUSB read failed with Windows error {error}; reconnecting.");
                        break;
                    }

                    if (bytesRead >= 2)
                    {
                        EmitInput(buffer, bytesRead);
                    }
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _log.Warn($"WinUSB backend error: {exception.Message}");
                Sleep(cancellationToken, TimeSpan.FromSeconds(2));
            }
        }
    }

    private void EmitInput(byte[] buffer, int bytesRead)
    {
        var button = (buffer[0] & 0x01) != 0;
        var rotation = unchecked((sbyte)buffer[1]);
        _log.Debug($"WinUSB raw event ({bytesRead} bytes): {BitConverter.ToString(buffer, 0, bytesRead)}");

        var direction = rotation switch
        {
            > 0 => KnobDirection.Clockwise,
            < 0 => KnobDirection.Counterclockwise,
            _ => KnobDirection.None
        };

        OnInputReceived(new KnobInput(button, direction, (uint)Math.Abs(rotation)));
    }

    private static void Sleep(CancellationToken cancellationToken, TimeSpan delay)
    {
        try
        {
            Task.Delay(delay, cancellationToken).Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
    }
}

internal readonly record struct KnobInput(bool IsPressed, KnobDirection Direction, uint RotationDistance);

internal enum KnobDirection
{
    None,
    Clockwise,
    Counterclockwise
}

internal static class WinUsbNative
{
    public const int ErrorSemTimeout = 121;

    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IEnumerable<string> EnumerateDevicePaths(Guid interfaceGuid)
    {
        var infoSet = SetupDiGetClassDevs(ref interfaceGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == InvalidHandleValue)
        {
            yield break;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    cbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                {
                    yield break;
                }

                SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize == 0)
                {
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            yield return path;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public static SafeFileHandle OpenDevice(string devicePath) =>
        CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        [MarshalAs(UnmanagedType.LPWStr)] string? enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        [Out] byte[] buffer,
        int bufferLength,
        out int lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    public sealed class SafeWinUsbHandle : SafeHandle
    {
        public SafeWinUsbHandle(IntPtr handle)
            : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => WinUsb_Free(handle);
    }
}

internal sealed class SystemVolume : IDisposable
{
    private readonly Logger _log;
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    public SystemVolume(Logger log)
    {
        _log = log;
    }

    public void Change(double delta)
    {
        var endpoint = GetEndpoint();
        var current = endpoint.AudioEndpointVolume.MasterVolumeLevelScalar;
        var next = Math.Clamp(current + (float)delta, 0f, 1f);
        endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = next;
        _log.Debug($"Volume {current:P0} -> {next:P0}");
    }

    public void ToggleMute()
    {
        var endpoint = GetEndpoint();
        var next = !endpoint.AudioEndpointVolume.Mute;
        endpoint.AudioEndpointVolume.Mute = next;
        _log.Debug(next ? "Muted." : "Unmuted.");
    }

    private MMDevice GetEndpoint()
    {
        if (_device is { State: DeviceState.Active })
        {
            return _device;
        }

        _device?.Dispose();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _log.Info($"Using default audio output: {_device.FriendlyName}");
        return _device;
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator.Dispose();
    }
}

internal sealed class Logger : IDisposable
{
    private readonly LogLevel _minimumLevel;
    private readonly StreamWriter _logFile;
    private readonly object _lock = new();

    public Logger(string logLevel)
    {
        _minimumLevel = Enum.TryParse<LogLevel>(logLevel, true, out var parsed) ? parsed : LogLevel.Info;
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerMateVolume");
        Directory.CreateDirectory(logDirectory);
        _logFile = new StreamWriter(Path.Combine(logDirectory, "PowerMateVolume.log"), append: true)
        {
            AutoFlush = true
        };
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";

        lock (_lock)
        {
            Console.WriteLine(line);
            _logFile.WriteLine(line);
        }
    }

    public void Dispose() => _logFile.Dispose();
}

internal enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}
