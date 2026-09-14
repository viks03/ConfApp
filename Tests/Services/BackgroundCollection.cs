// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Services;

/// <summary>
/// One start of the application for the questions that need the background
/// services to start FOR REAL: the cleanup cycle, and the lines the services
/// write to the log at startup.
/// <para>
/// Starting up is expensive — seconds — and the cleanup cycle is a single event
/// for the whole database, so this one start is shared between the classes rather
/// than each of them starting its own.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class BackgroundCollection : ICollectionFixture<CleanupRun>
{
    public const string Name = "BackgroundServices";
}
