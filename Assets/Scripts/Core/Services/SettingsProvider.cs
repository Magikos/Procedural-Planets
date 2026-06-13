public static class SettingsProvider
{
    public static ISettingsService Get() => ServiceLocator.GetWorld().Settings;

    public static TDto GetSettings<TDto>() => Get().GetSettings<TDto>();

    public static void Update<TDto>(TDto next) => Get().Update(next);

    public static void Register<TDto>(TDto initial) => Get().Register(initial);

    public static bool IsRegistered<TDto>() => Get().IsRegistered<TDto>();
}
