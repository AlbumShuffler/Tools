module AlbumShuffler.DataRetriever.Retrievers.YoutubeMusic

open System.Threading.Tasks
open AlbumShuffler.Shared

let retriever (s: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    Task.FromResult(Ok ({ Id = "not implemented"; Images = []; Name = "not implemented" }, List.empty<Outputs.Audiobook>))
