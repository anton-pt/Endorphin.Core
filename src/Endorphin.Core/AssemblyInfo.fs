// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Endorphin.Core")>]
[<assembly: AssemblyProductAttribute("Endorphin.Core")>]
[<assembly: AssemblyDescriptionAttribute("Core infrastructure for scientific instrument control software development in F#.")>]
[<assembly: AssemblyVersionAttribute("0.9.0")>]
[<assembly: AssemblyFileVersionAttribute("0.9.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.9.0"
    let [<Literal>] InformationalVersion = "0.9.0"
