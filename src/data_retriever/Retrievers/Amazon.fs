module AlbumShuffler.DataRetriever.Retrievers.Amazon

open AlbumShuffler.Shared

open System.Threading.Tasks

let retriever (s: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    Task.FromResult(Ok ({ Id = "not implemented"; Images = []; Name = "not implemented" }, List.empty<Outputs.Audiobook>))