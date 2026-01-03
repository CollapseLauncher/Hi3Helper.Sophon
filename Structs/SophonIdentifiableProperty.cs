using System;
using System.Text.Json.Serialization;
// ReSharper disable NonReadonlyMemberInGetHashCode

#nullable enable
namespace Hi3Helper.Sophon.Structs;

public class SophonIdentifiableProperty
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("matching_field")]
    public required string? MatchingField { get; set; }

    public bool Equals(SophonIdentifiableProperty? other) =>
        CategoryId == other?.CategoryId &&
        CategoryName == other.CategoryName &&
        MatchingField == other.MatchingField;

    public override bool Equals(object? obj) => obj is SophonIdentifiableProperty other && Equals(other);

    public override int GetHashCode() =>
#if NET6_0_OR_GREATER
        HashCode.Combine(CategoryId,
                         CategoryName,
                         MatchingField);
#else
            CategoryId.GetHashCode() ^
            CategoryName.GetHashCode() ^
            MatchingField.GetHashCode();
#endif
}
