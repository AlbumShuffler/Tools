module AlbumShuffler.DataRetriever.Retrievers.RetrieverIds


type RetrieverId = RetrieverId of string

let create = RetrieverId

let value = function RetrieverId s -> s

