using hotjob.DTOs.Authentication;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace hotjob.Swagger;

/// <summary>Prefills Swagger request bodies with the demo seed values so Try It Out needs no typing.</summary>
public sealed class RequestExampleSchemaFilter : ISchemaFilter
{
    private static readonly string FutureDate = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-ddTHH:mm:ssZ");
    private static readonly string InterviewDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static readonly Dictionary<Type, OpenApiObject> Examples = new()
    {
        [typeof(LoginRequest)] = Obj(
            ("email", Str("candidate@example.com")),
            ("password", Str("Candidate123!"))),

        [typeof(RegisterAccountRequest)] = Obj(
            ("email", Str("new.candidate@example.com")),
            ("password", Str("Candidate123!")),
            ("fullName", Str("New Candidate")),
            ("position", Str("Backend Developer")),
            ("accountType", Int(1)),
            ("companyName", Null())),

        [typeof(UpgradeToEmployerRequest)] = Obj(
            ("fullName", Str("Demo Candidate")),
            ("position", Str("HR Manager")),
            ("companyName", Str("Demo Candidate Co"))),

        [typeof(UpdateProfileRequest)] = Obj(
            ("fullName", Str("Demo Candidate")),
            ("dateOfBirth", Str("2000-01-15")),
            ("gender", Int(0)),
            ("address", Str("Ho Chi Minh City")),
            ("position", Str("Backend Developer")),
            ("summary", Str("3 years building .NET APIs and SQL databases.")),
            ("jobSeekingStatus", Str("Actively looking")),
            ("preferredWorkModel", Str("Hybrid")),
            ("preferredLocation", Str("Ho Chi Minh City"))),

        [typeof(ChangePasswordRequest)] = Obj(
            ("currentPassword", Str("Candidate123!")),
            ("newPassword", Str("NewCandidate123!"))),

        [typeof(CreateJobRequest)] = JobExample(),
        [typeof(UpdateJobRequest)] = JobExample(),

        [typeof(AssignCategoriesRequest)] = Obj(
            ("categoryIds", Arr(Int(1)))),

        [typeof(AssignSkillsRequest)] = Obj(
            ("skills", Arr(SkillExample()))),

        [typeof(UpdateStatusRequest)] = Obj(
            ("status", Int(2)),
            ("notes", Str("Strong .NET background; moving to shortlist."))),

        [typeof(SendNotificationRequest)] = Obj(
            ("title", Str("Interview invitation")),
            ("content", Str("We would like to schedule an interview this week."))),

        [typeof(CreateInterviewRequest)] = Obj(
            ("interviewDate", Str(InterviewDate)),
            ("locationOrLink", Str("https://meet.example.com/demo")),
            ("interviewType", Int(0)),
            ("notes", Str("30-minute technical screen."))),

        [typeof(UpdateInterviewRequest)] = Obj(
            ("interviewDate", Str(InterviewDate)),
            ("locationOrLink", Str("https://meet.example.com/demo")),
            ("interviewType", Int(0)),
            ("notes", Str("Rescheduled to next week.")),
            ("status", Int(0))),

        [typeof(UpdateCompanyRequest)] = Obj(
            ("website", Str("https://acme.example.com")),
            ("industry", Str("Software")),
            ("location", Str("Ho Chi Minh City")),
            ("scale", Int(2)),
            ("description", Str("Demo company for local Swagger testing."))),
    };

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (Examples.TryGetValue(context.Type, out var example))
            schema.Example = example;
    }

    private static OpenApiObject JobExample() => Obj(
        ("title", Str("Senior .NET Developer")),
        ("description", Str("Build and maintain .NET APIs for the demo job board.")),
        ("salaryMin", Int(1500)),
        ("salaryMax", Int(3000)),
        ("experienceRequired", Int(3)),
        ("jobType", Int(0)),
        ("expiryDate", Str(FutureDate)),
        ("status", Int(1)),
        ("categoryIds", Arr(Int(1))),
        ("skills", Arr(SkillExample())));

    private static OpenApiObject SkillExample() => Obj(
        ("skillId", Int(1)),
        ("importanceWeight", Double(8)));

    private static OpenApiObject Obj(params (string Key, IOpenApiAny Value)[] properties)
    {
        var obj = new OpenApiObject();
        foreach (var (key, value) in properties)
            obj[key] = value;
        return obj;
    }

    private static OpenApiArray Arr(params IOpenApiAny[] items)
    {
        var array = new OpenApiArray();
        array.AddRange(items);
        return array;
    }

    private static OpenApiString Str(string value) => new(value);
    private static OpenApiInteger Int(int value) => new(value);
    private static OpenApiDouble Double(double value) => new(value);
    private static OpenApiNull Null() => new();
}
