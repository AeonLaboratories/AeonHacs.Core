using System;
using System.IO.Ports;
using System.Linq;
using System.Management;

namespace AeonHacs.Utilities;

public static class SerialPortMonitor
{
    static string[] serialPorts;
    static ManagementEventWatcher portArrivalWatcher;
    static ManagementEventWatcher portRemovalWatcher;

    public static Action<string> PortArrived;
    public static Action<string> PortRemoved;

    static SerialPortMonitor()
    {
        serialPorts = SerialPort.GetPortNames();
        monitorDeviceChanges();
    }

    static void monitorDeviceChanges()
    {
        try
        {
            var deviceArrivalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
            var deviceRemovalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

            portArrivalWatcher = new ManagementEventWatcher(deviceArrivalQuery);
            portRemovalWatcher = new ManagementEventWatcher(deviceRemovalQuery);

            portArrivalWatcher.EventArrived += (o, args) => deviceAdded();
            portRemovalWatcher.EventArrived += (o, args) => deviceRemoved();

            portArrivalWatcher.Start();
            portRemovalWatcher.Start();
        }
        catch { }
    }

    static void deviceAdded()
    {
        lock(serialPorts)
        {
            var portNames = SerialPort.GetPortNames();
            var added = portNames.Except(serialPorts).ToArray();
            if (added.Length == 0) return;
            serialPorts = portNames;
            foreach (string port in added)
            {
                PortArrived?.Invoke(port);
            }
        }
    }

    static void deviceRemoved()
    {
        lock (serialPorts)
        {
            var portNames = SerialPort.GetPortNames();
            var removed = serialPorts.Except(portNames).ToArray();
            if (removed.Length == 0) return;
            serialPorts = portNames;
            foreach (string port in removed)
            {
                PortRemoved?.Invoke(port);
            }
        }
    }

    public static void Stop()
    {
        portArrivalWatcher?.Stop();
        portRemovalWatcher?.Stop();
    }
}
