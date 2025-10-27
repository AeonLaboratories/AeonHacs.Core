using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AeonHacs.Utilities;

// This class provides a way to handle an exception that can occur
// when a USB-Serial adapter is disconnected. The problem stems
// from the way .NET handles the underlying stream in the case
// of a serial port disappearing: it doesn't allow the stream
// to be closed after the serial port is disconnected.
//
// The exception occurs during BaseStream's Finalize() method,
// which is called by the garbage collector. The way this class
// gets around the problem is to inherit the .NET SerialPort
// class and override the Open() and Close() methods. During Open(),
// GC.SuppressFinalize(BaseStream) prevents BaseStream from
// being finalized prematurely. When Close() is called,
// BaseStream's Finalize() is re-enabled only if the SerialPort
// is still open. (An alternative, and perhaps more robust
// technique, would be to use try/catch to trap the exception
// that would be raised by GC.ReRegisterForFinalize(BaseStream)
// if the USB-Serial adapter were pulled out.)
public class USBSerialPort : SerialPort
{
    public new void Open()
    {
        if (!base.IsOpen)
        {
            base.Open();
            GC.SuppressFinalize(BaseStream);
        }
    }

    public new void Close()
    {
        //if (base.IsOpen)
        //{
        //    GC.ReRegisterForFinalize(BaseStream);
        //    base.Close();
        //}
        // Perhaps this alternative is superior:
        try { GC.ReRegisterForFinalize(BaseStream); } catch {}
        if (base.IsOpen) base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearRtsControlToggle();        // does nothing unless needed as described below

        try { base.Dispose(disposing); }
        catch { /* ignore exception - bug with USB-serial adapters. */ }
    }

    //
    // The section below enables the use of the Windows API's RTS_CONTROL_TOGGLE mode,
    // which is used by some older RS-485 devices and adapters.
    //

    public void SetRtsControlToggle()
    {
        if (IsOpen && BaseStream == null)
            throw new InvalidOperationException("Cannot set RTS_CONTROL_TOGGLE until after the port has been opened.");
        SetDcbFlag(12, 3); // flag 12 is fRtsControl, value 3 is RTS_CONTROL_TOGGLE
        RtsControlToggleSet = true;
    }

    /// <summary>
    /// Sets the RTS control mode to Disabled if it's currently
    /// configured to Toggle; otherwise, does nothing.
    /// </summary>
    public void ClearRtsControlToggle()
    {
        if (IsOpen && BaseStream != null && RtsControlToggleSet)
        {
            SetDcbFlag(12, 0); // flag 12 is fRtsControl, value 0 is RTS_CONTROL_DISABLE
            RtsControlToggleSet = false;
        }
    }

    private bool RtsControlToggleSet = false;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle hFile, IntPtr lpDCB);

    private void SetDcbFlag(int flag, int value)
    {
        // Get the base stream and its type
        object baseStream = BaseStream;
        Type baseStreamType = baseStream.GetType();

        // Record current DCB Flag value so we can restore it on failure
        object originalDcbFlag = baseStreamType.GetMethod("GetDcbFlag", BindingFlags.NonPublic | BindingFlags.Instance)
          .Invoke(baseStream, new object[] { flag });

        // Invoke the private method SetDcbFlag to change flag to value
        baseStreamType.GetMethod("SetDcbFlag", BindingFlags.NonPublic | BindingFlags.Instance)
          .Invoke(baseStream, new object[] { flag, value });

        try
        {
            // Get the Win32 file handle for the port
            SafeFileHandle handle = (SafeFileHandle)baseStreamType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(baseStream);

            // Box the private DCB field
            object dcb = baseStreamType.GetField("dcb", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(baseStream);

            // Create unmanaged memory to copy dcb field into
            IntPtr hGlobal = Marshal.AllocHGlobal(Marshal.SizeOf(dcb));
            try
            {
                // Copy dcb field to unmanaged memory
                Marshal.StructureToPtr(dcb, hGlobal, false);

                // Call SetCommState
                if (!SetCommState(handle, hGlobal))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                // Free the unmanaged memory
                Marshal.FreeHGlobal(hGlobal);
            }
        }
        catch
        {
            // Restore original DCB Flag value if we failed to update the device
            baseStreamType.GetMethod("SetDcbFlag", BindingFlags.NonPublic | BindingFlags.Instance)
              .Invoke(baseStream, new object[] { flag, originalDcbFlag });
            throw;
        }
    }

}
