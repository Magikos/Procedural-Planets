public interface ISettingsService
{
    TDto GetSettings<TDto>() where TDto : struct;
    void Update<TDto>(TDto next) where TDto : struct;
}
