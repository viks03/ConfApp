// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The forms a participant submits, in one place because both part 4 (the
/// profile) and part 5 (files) use them.
/// </summary>
public static class ParticipantForms
{
    /// <summary>
    /// The fields of the form on the profile page. Every submission carries all
    /// of them, which is how the browser sends them too: a missing field means
    /// "clear this", not "leave it as it was".
    /// </summary>
    public static Dictionary<string, string> Profile(
        ApplicationUser user,
        string? firstName      = null,
        string? lastName       = null,
        string? age            = null,
        string? academicTitle  = null,
        string? phone          = null,
        string? workplace      = null,
        string? partForm       = null,
        bool?   isForeigner    = null,
        bool?   wantsMarketing = null) => new()
    {
        ["Input.FirstName"]      = firstName     ?? user.FirstName,
        ["Input.LastName"]       = lastName      ?? user.LastName,
        ["Input.Age"]            = age           ?? user.Age.ToString(),
        ["Input.AcademicTitle"]  = academicTitle ?? user.AcademicTitle,
        ["Input.Phone"]          = phone         ?? user.PhoneNumber ?? string.Empty,
        ["Input.Workplace"]      = workplace     ?? user.Workplace,
        ["Input.PartForm"]       = partForm      ?? user.PartForm,
        ["Input.IsForeigner"]    = (isForeigner    ?? user.IsForeigner).ToString().ToLowerInvariant(),
        ["Input.WantsMarketing"] = (wantsMarketing ?? user.WantsMarketing).ToString().ToLowerInvariant()
    };
}
