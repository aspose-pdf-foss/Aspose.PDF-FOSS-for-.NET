namespace Aspose.Pdf.Annotations;

/// <summary>Named-icon style for <see cref="TextAnnotation"/> (PDF 32000 §12.5.6.4 /Name entries).</summary>
public enum TextIcon
{
    Note = 0,
    Comment = 1,
    Key = 2,
    Help = 3,
    NewParagraph = 4,
    Paragraph = 5,
    Insert = 6,
    Check = 7,
    Circle = 8,
    Cross = 9,
    Star = 10,
}

/// <summary>Named-icon style for <see cref="FileAttachmentAnnotation"/> (/Name entry).</summary>
public enum FileIcon
{
    PushPin = 0,
    Graph = 1,
    Paperclip = 2,
    Tag = 3,
}

/// <summary>CMYK channel selector used by <see cref="ColorBarAnnotation"/>.</summary>
public enum ColorsOfCMYK
{
    Cyan = 0,
    Magenta = 1,
    Yellow = 2,
    Black = 3,
}

/// <summary>Side a printer-mark annotation sits on.</summary>
public enum PrinterMarkSidePosition
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
}

/// <summary>Corner a printer-mark annotation sits in (used by bleed-/trim-marks).</summary>
public enum PrinterMarkCornerPosition
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3,
}

/// <summary>Named viewer commands targeted by a <see cref="NamedAction"/> (PDF 32000 §12.6.4.11).</summary>
public enum PredefinedAction
{
    NextPage = 0,
    PrevPage = 1,
    FirstPage = 2,
    LastPage = 3,
    Print = 4,
    PrintDialog = 5,
    Bookmarks_ExpanCurrentBookmark = 6,
    Bookmarks_HightlightCurrentBookmark = 7,
    Document_AttachFile = 8,
    Document_CropPages = 9,
    Document_DeletePages = 10,
    Document_ExtractPages = 11,
    Document_InsertPages = 12,
    Document_ReplacePages = 13,
    Document_RotatePages = 14,
    Edit_CheckSpelling_InComFieldEdit = 15,
    Edit_Find = 16,
    Edit_Preferences = 17,
    Edit_Search = 18,
    File_AttachToEmail = 19,
    File_Close = 20,
    File_CreatePDF_FromScanner = 21,
    File_CreatePDF_FromWebPage = 22,
    File_Exit = 23,
    File_Organizer_OpenOrganizer = 24,
    File_Print = 25,
    File_Properties = 26,
    File_SaveAs = 27,
    Miscellaneous_ZoomIn = 28,
    Miscellaneous_ZoomOut = 29,
    PageImages_PrintPages = 30,
    View_GoTo_NextView = 31,
    View_GoTo_Page = 32,
    View_GoTo_PreDocument = 33,
    View_GoTo_PreView = 34,
    View_NavigationPanels_Articles = 35,
    View_NavigationPanels_Attachments = 36,
    View_NavigationPanels_Boomarks = 37,
    View_NavigationPanels_Comments = 38,
    View_NavigationPanels_Fields = 39,
    View_NavigationPanels_Layers = 40,
    View_NavigationPanels_ModelTree = 41,
    View_NavigationPanels_Pages = 42,
    View_NavigationPanels_Signatures = 43,
    View_PageDisplay_SinglePage = 44,
    View_PageDisplay_SinglePageContinuous = 45,
    View_PageDisplay_TwoUp = 46,
    View_PageDisplay_TwoUpContinuous = 47,
    View_Toolbars_AdvanceEditing = 48,
    View_Toolbars_CommentMarkup = 49,
    View_Toolbars_Edit = 50,
    View_Toolbars_File = 51,
    View_Toolbars_Find = 52,
    View_Toolbars_Forms = 53,
    View_Toolbars_Measuring = 54,
    View_Toolbars_ObjectData = 55,
    View_Toolbars_PageDisplay = 56,
    View_Toolbars_PageNavigation = 57,
    View_Toolbars_PrintProduction = 58,
    View_Toolbars_PropertiesBar = 59,
    View_Toolbars_Redaction = 60,
    View_Toolbars_SelectZoom = 61,
    View_Toolbars_Tasks = 62,
    View_Toolbars_Typewriter = 63,
    View_Zoom_ActualSize = 64,
    View_Zoom_FitHeight = 65,
    View_Zoom_FitPage = 66,
    View_Zoom_FitVisible = 67,
    View_Zoom_FitWidth = 68,
    View_Zoom_ZoomTo = 69,
    Window_FullScreenMode = 70,
}
