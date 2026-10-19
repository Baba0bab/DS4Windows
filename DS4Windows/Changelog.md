## Version 4.1.1

**Controller detection**
* A controller left disabled in Windows (exclusive mode interrupted, or the re-enable step failed) is now enabled again by the next scan, so it is found again without a manual fix. The enable step is also retried up to three times

**Settings**
* App settings are saved when leaving the Settings tab, so they are no longer lost if DS4Windows is killed or closes abnormally
* The Hours/Days unit of "Check every" is now remembered, and values of 24 hours or more are no longer ignored

**System tray**
* A single left click on the tray icon opens the window, or minimizes it when it is already open
* "Open Program Folder" moved from the tray menu to the Utils group of the Settings tab

## Version 4.1.0

**Updates**
* Automatic update for copies installed with the setup program: accepting an update downloads the new installer from the release, checks its SHA-256 checksum against the release's SHA256SUMS.txt, then installs it silently and starts DS4Windows again. An all-users install in Program Files asks for administrator rights
* Portable copies (zip or folder), or a failed download, still open the release page, now with a message explaining why

## Version 4.0.0

First release of the Baba0bab fork, based on DS4Windows 3.9.9 by schmaldeo.

**Platform**
* Now targets .NET 10 (LTS). .NET 8 support ends on 2026-11-10
* Updated NLog 5.5.1, System.Management 10.0.13, TaskScheduler 2.12.2, WpfScreenHelper 2.1.1 and CsWin32 0.3.363

**User interface**
* Modernized theme (light and dark) with Windows 11 style controls: rounded corners, flat surfaces, accent states, thin scrollbars, underline tabs, card group boxes. Every window keeps the same layout
* The native title bar now follows the selected theme
* Start/Stop and Save use the accent button style
* Profile editor now shows the rainbow lightbar state when a profile is loaded
* Controller Readings refreshes on the UI thread and never waits on the input thread, so a busy interface can no longer stack up refreshes or stall a thread. It only redraws when a new report arrived (about 3x less CPU with the panel open)

**Controller connection (HID)**
* Fixed device handles never being closed, which leaked handles and could leave a controller locked in exclusive mode after a disconnect
* Fixed timed reads (Joy-Con, Switch Pro) leaving a pending request writing into freed memory. Timeouts are now reported as timeouts instead of read errors
* Reads no longer allocate a kernel event per report: 0 B allocated and about 3x less CPU per read
* Feature, serial and calibration reads no longer touch an already closed handle
* Fixed a crash when Windows returns a device description without a terminating null character

**Controller detection and hotplug**
* One faulty device no longer aborts the whole controller scan, and devices opened but not kept are closed
* Fixed a lost hotplug event when a controller was plugged in just as detection finished, and the hotplug worker now survives exceptions
* Joy-Con USB handshake can no longer spin forever and freeze detection when unplugged during init
* Joy-Con and Switch Pro now wait for the reply matching each subcommand, which fixes occasional wrong stick calibration
* Fixed an out-of-range slot at startup with more controllers than slots
* Stopping a controller now waits for its input thread to really exit
* No more bogus "read failure: 995" warning on an intentional stop

**Input processing**
* Rewrote the button debouncer: no per-report allocation, bounces filtered on both press and release, setting applied live, and disconnected controllers are no longer kept in memory

**Stability**
* An invalid UDP server address no longer crashes the app or blocks startup
* Errors during special actions no longer crash the app
* The "last update check" date is now stored culture-invariant (day and month were swapped on non-US systems)

**Updates**
* The update check now looks for releases of this fork instead of the discontinued upstream project. Accepting an update opens its release page; DS4Updater is no longer downloaded
* "Skip this version" is now honored by the automatic check at startup
* Releases ship an installer, a portable zip and SHA-256 checksums

**Tests and documentation**
* Fixed outdated test fixtures and added debouncer tests (16 of 16 tests pass)
* Added build and install guide (COMPIL.md) and a summary of the fixes (AMELIORATIONS.md)
