public interface ISettingsService
{
    TDto GetSettings<TDto>();
    void Update<TDto>(TDto next);
}
