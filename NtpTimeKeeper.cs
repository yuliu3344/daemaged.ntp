﻿using System;
using System.IO;
using System.Threading;

namespace Daemaged.NTP
{
  /// <summary>
  /// Utility class that spins up a background thread to keep the NTP time offset updated
  /// </summary>
  public static class NtpTimeKeeper
  {
    #region Private state

    private static int _sleepPeriod;
    private static Thread _syncThread;
    private static long _timeDiff;
    private static int _stratum;
    private static DateTime _lastTransmit;
    private static Ntp _ntp;
    private static readonly ManualResetEvent _stopEvent;

    #endregion

    #region Configuration settings

    /// <summary>
    /// The NTP host to connect to. Defaults to pool.ntp.org.
    /// </summary>
    public static string NtpHost { get; set; }

    /// <summary>
    /// The number of seconds to wait between NTP synchronizations. Defaults to 60.
    /// </summary>
    public static int PollingTime { get; set; }

    /// <summary>
    /// The number of milliseconds to wait for an NTP response before an exception will be thrown. Defaults to 3000.
    /// </summary>
    public static int Timeout { get; set; }

    #endregion

    /// <summary>
    /// An event that will be fired every time a sync fails.
    /// </summary>
    public static EventHandler<ErrorEventArgs> FailedSync;

    /// <summary>
    /// An event that will be fired every time a sync succeeds.
    /// </summary>
    public static EventHandler Synced { get; set; }

    static NtpTimeKeeper()
    {
      _stopEvent = new ManualResetEvent(false);
      NtpHost = "pool.ntp.org";
      Timeout = 3000;
      PollingTime = 60;
    }

    /// <summary>
    /// Start collecting NTP time differences
    /// </summary>
    public static void StartAutoSync()
    {
      if (_syncThread != null)
        throw new InvalidOperationException("Timekeeper is already started");

      _ntp = new Ntp(NtpHost) {VersionNumber = 4, Timeout = Timeout};

      Sync();

      _sleepPeriod = PollingTime*1000;
      _stopEvent.Reset();

      // Assuming this worked at least once, lets do it again...
      _syncThread = new Thread(KeepNtpTime)
                      {
                        IsBackground = true,
                        Name = "NtpTimeKeeper"
                      };
      _syncThread.Start();
    }

    /// <summary>
    /// Stop collecting time offsets
    /// </summary>
    public static void Stop()
    {
      if (!IsSyncing)
        return;
      _stopEvent.Set();
      _syncThread.Join();
      _syncThread = null;
    }

    public static bool IsSyncing
    {
      get { return _syncThread != null; }
    }

    private static void KeepNtpTime()
    {
      // Loop forever, syncing every _sleep period, until the stop event is set
      while (!_stopEvent.WaitOne(_sleepPeriod))
      {
        try
        {
          Sync();
          if (Synced != null)
            Synced(null, null);
        }
        catch (Exception e)
        {
          if (FailedSync != null)
            FailedSync(null, new ErrorEventArgs(e));
        }
      }
    }

    private static void Sync()
    {
      var resp = _ntp.GetTime();
      _timeDiff = resp.TimeOffset.ToTicks();
      _lastTransmit = resp.Packet.TransmitTimestamp.ToUniversalTime();
      _stratum = resp.Stratum;
    }

    #region Time accessors

    /// <summary>
    /// UtcNow  adjusted to the latest recovered time offset in ticks
    /// </summary>
    public static long AdjustedUtcNowTicks
    {
      get { return DateTime.UtcNow.Ticks + _timeDiff; }
    }

    /// <summary>
    /// UtcNow adjusted to the latest recovered time offset
    /// </summary>
    public static DateTime AdjustedUtcNow
    {
      get { return DateTime.UtcNow.AddTicks(_timeDiff); }
    }

    /// <summary>
    /// The latest recovered time offset in ticks
    /// </summary>
    public static long LastRecordedDiffTime
    {
      get { return _timeDiff; }
    }

    /// <summary>
    /// The NTP server's Stratum value
    /// </summary>
    public static int Stratum
    {
      get { return _stratum; }
    }

    /// <summary>
    /// The time-stamp when the last successful transition from the server was made
    /// </summary>
    public static DateTime LastTransmitTime
    {
      get { return _lastTransmit; }
    }

    public static TimeSpan TimeSinceLastSuccessfulSync
    {
      get { return DateTime.UtcNow - LastTransmitTime + TimeSpan.FromTicks(_timeDiff); }
    }

    #endregion
  }
}
