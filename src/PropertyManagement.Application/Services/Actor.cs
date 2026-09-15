namespace PropertyManagement.Application.Services;

/// <summary>
/// Who is performing an action. Every service that writes an audit trail takes one, so it lives on
/// its own rather than inside whichever service happened to need it first.
///
/// The name is copied onto audit entries as they are written rather than joined at read time: a
/// history should say who did something under the name they had at the time, and it should survive
/// that account being renamed or removed.
/// </summary>
public record Actor(string UserId, string Name);
