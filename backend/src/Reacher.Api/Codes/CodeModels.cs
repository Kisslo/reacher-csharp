namespace Reacher.Api.Codes;

/// <summary>One row of a code list. The name is Swedish, as SCB delivers it.</summary>
public sealed record CodeResponse(string Code, string Name);

public sealed record CodeProblem(string Type, string Title, int Status, string Code);
