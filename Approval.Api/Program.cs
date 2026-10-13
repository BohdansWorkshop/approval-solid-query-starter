using System.Text.Json.Serialization;
using Approval.Api.Workflow;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApprovalTaskStore, InMemoryApprovalTaskStore>();
builder.Services.AddSingleton<WorkflowService>();

var app = builder.Build();

static IResult Respond(OperationResult<TaskSnapshot> result) =>
    result.Value is not null
        ? Results.Json(result.Value, statusCode: result.StatusCode)
        : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);

app.MapPost("/api/approval-tasks", (CreateTaskRequest request, WorkflowService workflow) =>
{
    var result = workflow.Create(request);
    return result.Value is not null
        ? Results.Created($"/api/approval-tasks/{result.Value.Id}", result.Value)
        : Respond(result);
});
app.MapPost("/api/approval-tasks/{id:guid}/actions", (Guid id, ActionRequest request, WorkflowService workflow) =>
    Respond(workflow.Execute(id, request)));
app.MapGet("/api/approval-tasks/{id:guid}", (Guid id, WorkflowService workflow) => Respond(workflow.Get(id)));

app.Run();
