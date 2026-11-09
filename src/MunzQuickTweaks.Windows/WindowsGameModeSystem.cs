using System.ServiceProcess;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

public sealed class WindowsGameModeSystem : IGameModeSystem
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);
    private readonly WindowsPowerPlans _plans = new();

    public bool IsModernStandby => !_plans.IsSupported;

    public Guid GetActivePlan() => _plans.GetActive();

    public bool PlanExists(Guid plan) => _plans.Exists(plan);

    public void SetActivePlan(Guid plan) => _plans.SetActive(plan);

    public Guid? GetOverlay() => WindowsPowerModes.GetOverlayId();

    public void SetOverlay(Guid overlay) => WindowsPowerModes.SetOverlayId(overlay);

    public bool IsServiceRunning(string service)
    {
        using var controller = new ServiceController(service);
        try
        {
            return controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch (InvalidOperationException)
        {
            // Dienst existiert nicht
            return false;
        }
    }

    public void StopService(string service)
    {
        using var controller = new ServiceController(service);
        if (!controller.CanStop)
        {
            throw new InvalidOperationException($"Der Dienst {service} lässt sich gerade nicht anhalten.");
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
    }

    public void StartService(string service)
    {
        using var controller = new ServiceController(service);
        if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
    }
}
