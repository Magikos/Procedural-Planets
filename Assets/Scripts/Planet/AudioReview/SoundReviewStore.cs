using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public enum SoundReviewDecision { Unreviewed, Keep, Reject, Unsure }

[Serializable]
public struct SoundReview
{
    public string Id;
    public SoundReviewDecision Decision;
    public string Notes;
    public float Volume;
    public bool Loop;
}

public sealed class SoundReviewStore
{
    [Serializable]
    sealed class Document
    {
        public int Version = 1;
        public List<SoundReview> Reviews = new();
    }

    readonly Dictionary<string, SoundReview> _reviews = new(StringComparer.Ordinal);
    public string FilePath { get; }

    public SoundReviewStore(string path)
    {
        FilePath = Path.GetFullPath(path);
        if (!File.Exists(FilePath)) return;
        var document = JsonUtility.FromJson<Document>(File.ReadAllText(FilePath));
        if (document == null || document.Version != 1 || document.Reviews == null)
            throw new InvalidDataException("Unsupported sound review file. Preserve the file before repairing it.");
        foreach (var review in document.Reviews)
        {
            Validate(review);
            if (!_reviews.TryAdd(review.Id, review))
                throw new InvalidDataException("Duplicate sound review ID: " + review.Id);
        }
    }

    public SoundReview Get(string id, bool loop) => _reviews.TryGetValue(id, out var review)
        ? review : new SoundReview { Id = id, Notes = "", Volume = .5f, Loop = loop };

    public void Save(SoundReview review)
    {
        Validate(review);
        var document = new Document();
        foreach (var existing in _reviews.Values)
            if (existing.Id != review.Id) document.Reviews.Add(existing);
        document.Reviews.Add(review);
        document.Reviews.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonUtility.ToJson(document, true), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
            _reviews[review.Id] = review;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    static void Validate(SoundReview review)
    {
        if (string.IsNullOrWhiteSpace(review.Id) || !Enum.IsDefined(typeof(SoundReviewDecision), review.Decision)
            || float.IsNaN(review.Volume) || float.IsInfinity(review.Volume)
            || review.Volume < 0 || review.Volume > 1)
            throw new InvalidDataException("Invalid sound review entry: " + review.Id);
    }
}
