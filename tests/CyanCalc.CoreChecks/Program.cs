try
{
    if (args.Any(arg => arg is not ("--reference-utils" or "--scalar-readers" or "--hyperlinks" or "--workbook-library" or "--input-policy")))
        throw new ArgumentException("Unknown core-check option");
    WorkbookInputChecks.Run(); Console.WriteLine("WORKBOOK_INPUT_CHECKS_PASS");
    if (args.Contains("--input-policy")) return 0;
    WorkbookLibraryChecks.Run(); Console.WriteLine("WORKBOOK_LIBRARY_CHECKS_PASS");
    if (args.Contains("--workbook-library")) return 0;
    ReferenceUtilsChecks.Run(); Console.WriteLine("REFERENCE_UTILS_CHECKS_PASS");
    if (args.Contains("--reference-utils")) return 0;
    ScalarReaderChecks.Run(); Console.WriteLine("SCALAR_READER_CHECKS_PASS");
    if (args.Contains("--scalar-readers")) return 0;
    HyperlinkChecks.Run(); Console.WriteLine("HYPERLINK_CHECKS_PASS");
    if (args.Contains("--hyperlinks")) return 0;
    EngineChecks.Run(); Console.WriteLine("ENGINE_CHECKS_PASS");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
