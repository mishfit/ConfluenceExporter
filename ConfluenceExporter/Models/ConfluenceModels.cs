using Newtonsoft.Json;

namespace ConfluenceExporter.Models;

public class ConfluenceSpace
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("key")]
    public string Key { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("authorId")]
    public string? AuthorId { get; set; }

    [JsonProperty("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonProperty("homepageId")]
    public string? HomepageId { get; set; }

    [JsonProperty("_expandable")]
    public ConfluenceExpandable? Expandable { get; set; }
}

public class ConfluencePage
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("spaceId")]
    public string? SpaceId { get; set; }

    [JsonProperty("space")]
    public ConfluenceSpace? Space { get; set; }

    [JsonProperty("body")]
    public ConfluenceV2Body? Body { get; set; }

    [JsonProperty("version")]
    public ConfluenceV2Version? Version { get; set; }

    [JsonProperty("ancestors")]
    public List<ConfluencePage> Ancestors { get; set; } = new();

    [JsonProperty("children")]
    public ConfluenceChildren? Children { get; set; }

    [JsonProperty("parentId")]
    public string? ParentId { get; set; }

    [JsonProperty("parentType")]
    public string? ParentType { get; set; }

    [JsonProperty("position")]
    public int? Position { get; set; }

    [JsonProperty("authorId")]
    public string? AuthorId { get; set; }

    [JsonProperty("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonProperty("_expandable")]
    public ConfluenceExpandable? Expandable { get; set; }
}

public class ConfluenceBody
{
    [JsonProperty("storage")]
    public ConfluenceStorage? Storage { get; set; }

    [JsonProperty("view")]
    public ConfluenceView? View { get; set; }
}

public class ConfluenceStorage
{
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("representation")]
    public string Representation { get; set; } = string.Empty;
}

public class ConfluenceView
{
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("representation")]
    public string Representation { get; set; } = string.Empty;
}

public class ConfluenceVersion
{
    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("when")]
    public DateTime When { get; set; }

    [JsonProperty("by")]
    public ConfluenceUser? By { get; set; }
}

public class ConfluenceUser
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;
}

public class ConfluenceChildren
{
    [JsonProperty("page")]
    public ConfluencePageResults? Page { get; set; }
}

public class ConfluencePageResults
{
    [JsonProperty("results")]
    public List<ConfluencePage> Results { get; set; } = new();

    [JsonProperty("start")]
    public int Start { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("_links")]
    public ConfluenceLinks? Links { get; set; }
}

public class ConfluenceLinks
{
    [JsonProperty("next")]
    public string? Next { get; set; }

    [JsonProperty("prev")]
    public string? Prev { get; set; }

    [JsonProperty("base")]
    public string Base { get; set; } = string.Empty;

    [JsonProperty("context")]
    public string Context { get; set; } = string.Empty;
}

public class ConfluenceExpandable
{
    [JsonProperty("children")]
    public string? Children { get; set; }

    [JsonProperty("descendants")]
    public string? Descendants { get; set; }

    [JsonProperty("body")]
    public string? Body { get; set; }

    [JsonProperty("space")]
    public string? Space { get; set; }

    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("ancestors")]
    public string? Ancestors { get; set; }
}

public class ConfluenceSearchResult
{
    [JsonProperty("results")]
    public List<ConfluencePage> Results { get; set; } = new();

    [JsonProperty("start")]
    public int Start { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("totalSize")]
    public int TotalSize { get; set; }

    [JsonProperty("_links")]
    public ConfluenceLinks? Links { get; set; }
}

public class ConfluenceSpacesResult
{
    [JsonProperty("results")]
    public List<ConfluenceSpace> Results { get; set; } = new();

    [JsonProperty("_links")]
    public ConfluenceV2Links? Links { get; set; }
}

public class ConfluenceV2Links
{
    [JsonProperty("next")]
    public string? Next { get; set; }
}

public class ConfluenceV2PagesResult
{
    [JsonProperty("results")]
    public List<ConfluencePage> Results { get; set; } = new();

    [JsonProperty("_links")]
    public ConfluenceV2Links? Links { get; set; }
}

public class ConfluenceV2ChildrenResult
{
    [JsonProperty("results")]
    public List<ConfluenceChild> Results { get; set; } = new();

    [JsonProperty("_links")]
    public ConfluenceV2Links? Links { get; set; }
}

public class ConfluenceChild
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("spaceId")]
    public string SpaceId { get; set; } = string.Empty;

    [JsonProperty("childPosition")]
    public int? ChildPosition { get; set; }
}

public class ConfluenceV2Body
{
    [JsonProperty("storage")]
    public ConfluenceStorage? Storage { get; set; }

    [JsonProperty("atlas_doc_format")]
    public ConfluenceAtlasDoc? AtlasDoc { get; set; }
}

public class ConfluenceAtlasDoc
{
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("representation")]
    public string Representation { get; set; } = string.Empty;
}

public class ConfluenceV2Version
{
    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("minorEdit")]
    public bool? MinorEdit { get; set; }

    [JsonProperty("authorId")]
    public string? AuthorId { get; set; }
}