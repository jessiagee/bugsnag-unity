﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AOT;
using UnityEngine;

namespace BugsnagUnity.Payload
{
  /// <summary>
  /// Represents the "device" key in the error report payload.
  /// </summary>
  public class Device : Dictionary<string, object>, IFilterable
  {
    internal Device() : this(Hostname)
    {
    }

    internal Device(string hostname)
    {
      this.AddToPayload("hostname", hostname);
      this.AddToPayload("locale", CultureInfo.CurrentCulture.ToString());
      this.AddToPayload("timezone", TimeZone.CurrentTimeZone.StandardName);
      this.AddToPayload("osName", OsName);
      this.AddToPayload("time", DateTime.UtcNow);

      var matches = Regex.Match(Environment.OSVersion.VersionString, "\\A(?<osName>[a-zA-Z ]*) (?<osVersion>[\\d\\.]*)\\z");
      if (matches.Success)
      {
        this.AddToPayload("osName", matches.Groups["osName"].Value);
        this.AddToPayload("osVersion", matches.Groups["osVersion"].Value);
      }
    }

    /// <summary>
    /// Resolve the hostname using either "COMPUTERNAME" (win) or "HOSTNAME" (*nix) environment variable.
    /// </summary>
    private static string Hostname
    {
      get
      {
        return Environment.GetEnvironmentVariable("COMPUTERNAME") ?? Environment.GetEnvironmentVariable("HOSTNAME");
      }
    }

    private static string OsName
    {
      get
      {
        return Environment.OSVersion.VersionString;
      }
    }
  }

  class AndroidDevice : Device
  {
    internal AndroidDevice(AndroidJavaObject client)
    {
      using (var deviceData = client.Call<AndroidJavaObject>("getDeviceData"))
      using (var map = deviceData.Call<AndroidJavaObject>("getDeviceData"))
      {
        this.PopulateDictionaryFromAndroidData(map);
      }
    }
  }

  class MacOsDevice : Device
  {
    [DllImport("bugsnag-osx", EntryPoint = "bugsnag_retrieveDeviceData")]
    static extern void RetrieveAppData(IntPtr instance, Action<IntPtr, string, string> populate);
    
    internal MacOsDevice()
    {
      var handle = GCHandle.Alloc(this);

      try
      {
        RetrieveAppData(GCHandle.ToIntPtr(handle), PopulateDeviceData);
      }
      finally
      {
        handle.Free();
      }
    }

    [MonoPInvokeCallback(typeof(Action<IntPtr, string, string>))]
    static void PopulateDeviceData(IntPtr instance, string key, string value)
    {
      var handle = GCHandle.FromIntPtr(instance);
      if (handle.Target is MacOsDevice app)
      {
        app.AddToPayload(key, value);
      }
    }
  }

  class iOSDevice : Device
  {
    [DllImport("__Internal", EntryPoint = "bugsnag_retrieveDeviceData")]
    static extern void RetrieveAppData(IntPtr instance, Action<IntPtr, string, string> populate);

    internal iOSDevice()
    {
      var handle = GCHandle.Alloc(this);

      try
      {
        RetrieveAppData(GCHandle.ToIntPtr(handle), PopulateDeviceData);
      }
      finally
      {
        handle.Free();
      }
    }

    [MonoPInvokeCallback(typeof(Action<IntPtr, string, string>))]
    static void PopulateDeviceData(IntPtr instance, string key, string value)
    {
      var handle = GCHandle.FromIntPtr(instance);
      if (handle.Target is iOSDevice app)
      {
        app.AddToPayload(key, value);
      }
    }
  }
}
