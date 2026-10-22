using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure.Data;
using OmniPrep.Infrastructure.Repositories;
using OmniPrep.Infrastructure.Services;

namespace OmniPrep.Infrastructure;

// ============================================================
// OmniPrep.Infrastructure - DI Registration Extension
// Đăng ký tất cả services vào DI Container
// ============================================================

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Đăng ký tất cả Infrastructure services vào DI container.
    /// Gọi từ Program.cs: builder.Services.AddInfrastructure(builder.Configuration)
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // =====================================================
        // DATABASE: EF Core + Supabase PostgreSQL + pgvector
        // =====================================================
        services.AddDbContext<OmniPrepDbContext>(options =>
        {
            var connStr = configuration.GetConnectionString("Supabase")
                ?? throw new InvalidOperationException("Thiếu connection string 'Supabase'");

            // Cấu hình Npgsql Connection Pool chống tràn kết nối Supabase Pooler (EMAXCONNSESSION)
            var npgsqlBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);
            if (npgsqlBuilder.MaxPoolSize > 8 || npgsqlBuilder.MaxPoolSize == 100)
            {
                npgsqlBuilder.MaxPoolSize = 8;
            }
            npgsqlBuilder.MinPoolSize = 0;
            npgsqlBuilder.ConnectionIdleLifetime = 10;
            npgsqlBuilder.ConnectionPruningInterval = 5;
            npgsqlBuilder.Timeout = 15;

            options.UseNpgsql(npgsqlBuilder.ConnectionString, npgsql =>
            {
                npgsql.UseVector();  // Kích hoạt pgvector
                // Register native PostgreSQL enums with the driver as well as EF.
                var enumNameTranslator = new UppercaseSnakeCaseNameTranslator();
                npgsql.MapEnum<SubjectCode>("subject_code", nameTranslator: enumNameTranslator);
                npgsql.MapEnum<QuestionFormat>("question_format", nameTranslator: enumNameTranslator);
                npgsql.MapEnum<DifficultyLevel>("difficulty_level", nameTranslator: enumNameTranslator);
                npgsql.MapEnum<IngestionStatus>("ingestion_status", nameTranslator: enumNameTranslator);
                npgsql.MapEnum<AnswerStatus>("answer_status", nameTranslator: enumNameTranslator);
                npgsql.MapEnum<UserRole>("user_role", nameTranslator: enumNameTranslator);
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            });

            // Chỉ bật detailed errors trong Development
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                options.EnableDetailedErrors().EnableSensitiveDataLogging();
        });

        // =====================================================
        // GEMINI API: Configuration & HttpClient
        // =====================================================
        services.Configure<GeminiOptions>(
            configuration.GetSection(GeminiOptions.SectionName));

        services.AddHttpClient<IGeminiService, GeminiService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);  // Bóc tách đề có thể lâu
        });

        services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // =====================================================
        // SUBJECT STRATEGIES (Strategy Pattern)
        // Thêm môn mới: chỉ cần thêm một dòng AddSingleton ở đây
        // =====================================================
        services.AddSingleton<ISubjectStrategy, GeographyStrategy>();   // Địa lý
        services.AddSingleton<ISubjectStrategy, GdktplStrategy>();      // GDKT&PL
        // TODO: services.AddSingleton<ISubjectStrategy, MathStrategy>();
        // TODO: services.AddSingleton<ISubjectStrategy, PhysicsStrategy>();

        services.AddSingleton<ISubjectStrategyFactory, SubjectStrategyFactory>();

        // =====================================================
        // REPOSITORIES
        // =====================================================
        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<IExamRepository, ExamRepository>();
        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IMasteryRepository, MasteryRepository>();
        services.AddScoped<IExamPaperRepository, ExamPaperRepository>();

        // =====================================================
        // DOMAIN SERVICES & PIPELINE
        // =====================================================
        services.AddSingleton<IQuestionValidator, OmniPrep.Infrastructure.Validation.QuestionValidator>();
        services.AddSingleton<IPdfDocumentExtractor, PdfDocumentExtractor>();
        services.AddSingleton<IExamDocumentFetcher, SafeDocumentFetcher>();
        services.AddSingleton<IExamDiscoveryProvider, CuratedSourcesDiscoveryProvider>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IGradingService, GradingService>();
        services.AddScoped<IExamGenerationService, ExamGenerationService>();
        services.AddHttpClient<IScraperService, ScraperService>();

        return services;
    }
}
