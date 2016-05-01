// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace Endorphin.Core

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module List =

    /// Creates a list from the elements contained in a set.
    let ofSet set = Set.toList set
    
    /// Cons an element onto a list.
    let cons list value = value :: list

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Map =

    /// Returns the values corresponding to an array of keys in a given map.
    let findArray keys map = keys |> Array.map (fun key -> Map.find key map)

    /// Get a set of the keys in a map.
    let keys map = Map.fold (fun set key _ -> Set.add key set) Set.empty map

    /// Get a set of the values in a map.
    let values map = Map.fold (fun set _ value -> Set.add value set) Set.empty map

    /// Create a union of two maps.
    let union map1 map2 = Map.fold (fun map key value -> Map.add key value map) map2 map1