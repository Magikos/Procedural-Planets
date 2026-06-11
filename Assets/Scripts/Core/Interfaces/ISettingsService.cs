public interface ISettingsService
{
    TDto GetSettings<TDto>();
    void Update<TDto>(TDto next);
    void Register<TDto>(TDto initial);
    bool IsRegistered<TDto>();
}
