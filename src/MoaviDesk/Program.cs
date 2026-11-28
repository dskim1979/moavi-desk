/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI;

namespace MoaviDesk;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // the installer writing the PC's settings (msiexec … SERVER=… ENROLL_CODE=…): no window
        if (args.Contains(MachineSetup.Switch)) { return MachineSetup.Run(args); }
        // the MOAVI Desk Agent service (enrolment, heartbeat, kiosk key): no window
        if (args.Contains(Agent.Switch)) { return Agent.Run(args); }

        var config = AppConfigManager.Load();
        ApplyLanguage(config.Language);
        // a desktop in MOAVI Desk's own RDP window (started by the main window, one process each)
        if (args.Contains(RdpWindow.Switch)) { return RdpWindow.Run(args, config); }

        Platform.Rdp = new WindowsRdp();
        return MoaviApp.RunDesktop(AppBuilder.Configure<Application>().UsePlatformDetect(), args, config);
    }
}
