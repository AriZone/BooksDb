using Microsoft.Extensions.DependencyInjection;
using BooksDb.Data;
using BooksDb.ViewModels;
using BooksDb.Data.Lookup;

namespace BooksDb;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBooksDbServices(this IServiceCollection services)
    {
        services.AddSingleton(new Database(Database.DefaultPath()));
        services.AddSingleton<BookRepository>();
        services.AddSingleton<LookupRepository>();
        services.AddSingleton<PersonRepository>();
        services.AddSingleton<ExcelImporter>();
        services.AddSingleton<BibliotekaNarodowaClient>();
        services.AddSingleton<GoogleBooksClient>();
        services.AddSingleton<OpenLibraryClient>();
        services.AddSingleton<PictureStore>();
        services.AddSingleton<PictureRepository>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<BorrowerRepository>();
        services.AddSingleton<LoanRepository>();
        services.AddSingleton<PendingCaptureRepository>();
        services.AddSingleton<CoverCache>();
        
        services.AddTransient<MainViewModel>();
        
        return services;
    }
}