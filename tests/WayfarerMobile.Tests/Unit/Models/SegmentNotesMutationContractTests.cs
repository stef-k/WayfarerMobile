using System.Text.Json;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Models;

public class SegmentNotesMutationContractTests
{
    [Fact]
    public void NotesRequest_DoesNotSubmitWaypointAssociations()
    {
        var json = JsonSerializer.Serialize(new SegmentNotesUpdateRequest { Notes = "updated" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("notes");
    }
}
