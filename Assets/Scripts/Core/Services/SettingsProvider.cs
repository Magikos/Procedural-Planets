public static class SettingsProvider
{
    static ISettingsService _fallback;

    public static ISettingsService Get()
    {
        if (_fallback != null)
            return _fallback;

        if (ServiceLocator.TryGet(out _fallback))
            return _fallback;

        return _fallback = ServiceLocator.Register<ISettingsService>(new SettingsService());
    }

    public static TDto GetSettings<TDto>() => Get().GetSettings<TDto>();

    public static void Update<TDto>(TDto next) => Get().Update(next);
}
