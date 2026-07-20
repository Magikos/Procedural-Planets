public static class SettingsProvider
{
    public static ISettingsService Get() => ServiceLocator.GetWorld().Settings;

    public static TDto GetSettings<TDto>() => Get().GetSettings<TDto>();

    public static void Update<TDto>(TDto next) => Get().Update(next);

    public static void Register<TDto>(TDto initial) => Get().Register(initial);

    public static bool IsRegistered<TDto>() => Get().IsRegistered<TDto>();

    public static bool TryGetFrozen<TDto>(out TDto dto)
    {
        dto = default;
        if (!ServiceLocator.HasActiveWorld)
            return false;

        ISettingsService settings = Get();
        if (!settings.IsFrozen || !settings.IsRegistered<TDto>())
            return false;

        dto = settings.GetSettings<TDto>();
        return true;
    }

    public static bool TryGetFrozen<TA, TB>(out TA a, out TB b)
    {
        a = default;
        b = default;
        if (!ServiceLocator.HasActiveWorld)
            return false;

        ISettingsService settings = Get();
        if (!settings.IsFrozen || !settings.IsRegistered<TA>() || !settings.IsRegistered<TB>())
            return false;

        a = settings.GetSettings<TA>();
        b = settings.GetSettings<TB>();
        return true;
    }
}
