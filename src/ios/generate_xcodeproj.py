import os
import uuid

def generate_id(seed: str) -> str:
    import hashlib
    h = hashlib.md5(seed.encode('utf-8')).hexdigest()[:24].upper()
    return h

project_dir = os.path.dirname(os.path.abspath(__file__))
xcodeproj_dir = os.path.join(project_dir, "irpotify.xcodeproj")
os.makedirs(xcodeproj_dir, exist_ok=True)
xcscheme_dir = os.path.join(xcodeproj_dir, "xcshareddata", "xcschemes")
os.makedirs(xcscheme_dir, exist_ok=True)

# List of files relative to src/ios
swift_files = [
    "irpotify/App/irpotifyApp.swift",
    "irpotify/Models/Models.swift",
    "irpotify/Services/SessionManager.swift",
    "irpotify/Services/NetworkService.swift",
    "irpotify/Services/AudioPlayerService.swift",
    "irpotify/Theme/AppTheme.swift",
    "irpotify/Views/Components/ArtworkAsyncImage.swift",
    "irpotify/Views/Components/TrackRowView.swift",
    "irpotify/Views/Components/MiniPlayerView.swift",
    "irpotify/Views/Player/NowPlayingView.swift",
    "irpotify/Views/Auth/QRScannerView.swift",
    "irpotify/Views/Auth/LoginView.swift",
    "irpotify/Views/Home/HomeView.swift",
    "irpotify/Views/Search/SearchView.swift",
    "irpotify/Views/Library/AlbumDetailView.swift",
    "irpotify/Views/Library/ArtistDetailView.swift",
    "irpotify/Views/Library/FolderDetailView.swift",
    "irpotify/Views/Library/LibraryView.swift",
    "irpotify/Views/Settings/SettingsView.swift",
    "irpotify/Views/Main/MainTabView.swift",
]

resource_files = [
    "irpotify/Resources/Assets.xcassets",
]

plist_file = "irpotify/Resources/Info.plist"

# Generate UUIDs for all objects
# File References
file_refs = {}
build_files = {}

for f in swift_files:
    fname = os.path.basename(f)
    f_ref = generate_id(f"FILEREF_{f}")
    b_file = generate_id(f"BUILDFILE_{f}")
    file_refs[f] = (f_ref, fname, "sourcecode.swift")
    build_files[f] = (b_file, f_ref, fname)

for r in resource_files:
    fname = os.path.basename(r)
    f_ref = generate_id(f"FILEREF_{r}")
    b_file = generate_id(f"BUILDFILE_{r}")
    file_refs[r] = (f_ref, fname, "folder.assetcatalog")
    build_files[r] = (b_file, f_ref, fname)

# Plist file reference (not in build files)
plist_ref = generate_id(f"FILEREF_{plist_file}")
file_refs[plist_file] = (plist_ref, os.path.basename(plist_file), "text.plist.xml")

# App product reference
product_ref = generate_id("FILEREF_irpotify.app")

# Groups
group_root_ref = generate_id("GROUP_ROOT")
group_irpotify_ref = generate_id("GROUP_irpotify")
group_app_ref = generate_id("GROUP_App")
group_models_ref = generate_id("GROUP_Models")
group_services_ref = generate_id("GROUP_Services")
group_theme_ref = generate_id("GROUP_Theme")
group_views_ref = generate_id("GROUP_Views")
group_views_auth_ref = generate_id("GROUP_Views_Auth")
group_views_comp_ref = generate_id("GROUP_Views_Components")
group_views_home_ref = generate_id("GROUP_Views_Home")
group_views_search_ref = generate_id("GROUP_Views_Search")
group_views_lib_ref = generate_id("GROUP_Views_Library")
group_views_player_ref = generate_id("GROUP_Views_Player")
group_views_settings_ref = generate_id("GROUP_Views_Settings")
group_views_main_ref = generate_id("GROUP_Views_Main")
group_resources_ref = generate_id("GROUP_Resources")
group_products_ref = generate_id("GROUP_Products")

# Build Phases
sources_phase_ref = generate_id("PHASE_Sources")
resources_phase_ref = generate_id("PHASE_Resources")
frameworks_phase_ref = generate_id("PHASE_Frameworks")

# Targets
target_ref = generate_id("TARGET_irpotify")

# Build Configurations
proj_cfg_debug_ref = generate_id("CONFIG_Proj_Debug")
proj_cfg_release_ref = generate_id("CONFIG_Proj_Release")
proj_cfg_list_ref = generate_id("CONFIGLIST_Proj")

target_cfg_debug_ref = generate_id("CONFIG_Target_Debug")
target_cfg_release_ref = generate_id("CONFIG_Target_Release")
target_cfg_list_ref = generate_id("CONFIGLIST_Target")

project_obj_ref = generate_id("PROJECT_irpotify")

# Construct PBXBuildFile section
pbx_build_file = []
for f, (b_id, f_id, fname) in build_files.items():
    pbx_build_file.append(f"\t\t{b_id} /* {fname} in Sources/Resources */ = {{isa = PBXBuildFile; fileRef = {f_id} /* {fname} */; }};")

# Construct PBXFileReference section
pbx_file_ref = []
for path, (f_id, fname, ftype) in file_refs.items():
    pbx_file_ref.append(f"\t\t{f_id} /* {fname} */ = {{isa = PBXFileReference; lastKnownFileType = {ftype}; path = {fname}; sourceTree = \"<group>\"; }};")
pbx_file_ref.append(f"\t\t{product_ref} /* irpotify.app */ = {{isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = irpotify.app; sourceTree = BUILT_PRODUCTS_DIR; }};")

# Groups structure
pbx_groups = []

# App group
pbx_groups.append(f"""\t\t{group_app_ref} /* App */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/App/irpotifyApp.swift'][0]} /* irpotifyApp.swift */,
\t\t\t);
\t\t\tpath = App;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Models group
pbx_groups.append(f"""\t\t{group_models_ref} /* Models */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Models/Models.swift'][0]} /* Models.swift */,
\t\t\t);
\t\t\tpath = Models;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Services group
pbx_groups.append(f"""\t\t{group_services_ref} /* Services */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Services/SessionManager.swift'][0]} /* SessionManager.swift */,
\t\t\t\t{file_refs['irpotify/Services/NetworkService.swift'][0]} /* NetworkService.swift */,
\t\t\t\t{file_refs['irpotify/Services/AudioPlayerService.swift'][0]} /* AudioPlayerService.swift */,
\t\t\t);
\t\t\tpath = Services;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Theme group
pbx_groups.append(f"""\t\t{group_theme_ref} /* Theme */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Theme/AppTheme.swift'][0]} /* AppTheme.swift */,
\t\t\t);
\t\t\tpath = Theme;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Views subgroups
pbx_groups.append(f"""\t\t{group_views_auth_ref} /* Auth */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Auth/LoginView.swift'][0]} /* LoginView.swift */,
\t\t\t\t{file_refs['irpotify/Views/Auth/QRScannerView.swift'][0]} /* QRScannerView.swift */,
\t\t\t);
\t\t\tpath = Auth;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_comp_ref} /* Components */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Components/ArtworkAsyncImage.swift'][0]} /* ArtworkAsyncImage.swift */,
\t\t\t\t{file_refs['irpotify/Views/Components/TrackRowView.swift'][0]} /* TrackRowView.swift */,
\t\t\t\t{file_refs['irpotify/Views/Components/MiniPlayerView.swift'][0]} /* MiniPlayerView.swift */,
\t\t\t);
\t\t\tpath = Components;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_home_ref} /* Home */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Home/HomeView.swift'][0]} /* HomeView.swift */,
\t\t\t);
\t\t\tpath = Home;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_search_ref} /* Search */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Search/SearchView.swift'][0]} /* SearchView.swift */,
\t\t\t);
\t\t\tpath = Search;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_lib_ref} /* Library */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Library/LibraryView.swift'][0]} /* LibraryView.swift */,
\t\t\t\t{file_refs['irpotify/Views/Library/AlbumDetailView.swift'][0]} /* AlbumDetailView.swift */,
\t\t\t\t{file_refs['irpotify/Views/Library/ArtistDetailView.swift'][0]} /* ArtistDetailView.swift */,
\t\t\t\t{file_refs['irpotify/Views/Library/FolderDetailView.swift'][0]} /* FolderDetailView.swift */,
\t\t\t);
\t\t\tpath = Library;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_player_ref} /* Player */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Player/NowPlayingView.swift'][0]} /* NowPlayingView.swift */,
\t\t\t);
\t\t\tpath = Player;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_settings_ref} /* Settings */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Settings/SettingsView.swift'][0]} /* SettingsView.swift */,
\t\t\t);
\t\t\tpath = Settings;
\t\t\tsourceTree = "<group>";
\t\t}};""")

pbx_groups.append(f"""\t\t{group_views_main_ref} /* Main */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Views/Main/MainTabView.swift'][0]} /* MainTabView.swift */,
\t\t\t);
\t\t\tpath = Main;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Views master group
pbx_groups.append(f"""\t\t{group_views_ref} /* Views */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{group_views_auth_ref} /* Auth */,
\t\t\t\t{group_views_comp_ref} /* Components */,
\t\t\t\t{group_views_home_ref} /* Home */,
\t\t\t\t{group_views_search_ref} /* Search */,
\t\t\t\t{group_views_lib_ref} /* Library */,
\t\t\t\t{group_views_player_ref} /* Player */,
\t\t\t\t{group_views_settings_ref} /* Settings */,
\t\t\t\t{group_views_main_ref} /* Main */,
\t\t\t);
\t\t\tpath = Views;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Resources group
pbx_groups.append(f"""\t\t{group_resources_ref} /* Resources */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{file_refs['irpotify/Resources/Assets.xcassets'][0]} /* Assets.xcassets */,
\t\t\t\t{file_refs['irpotify/Resources/Info.plist'][0]} /* Info.plist */,
\t\t\t);
\t\t\tpath = Resources;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# irpotify folder group
pbx_groups.append(f"""\t\t{group_irpotify_ref} /* irpotify */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{group_app_ref} /* App */,
\t\t\t\t{group_models_ref} /* Models */,
\t\t\t\t{group_services_ref} /* Services */,
\t\t\t\t{group_theme_ref} /* Theme */,
\t\t\t\t{group_views_ref} /* Views */,
\t\t\t\t{group_resources_ref} /* Resources */,
\t\t\t);
\t\t\tpath = irpotify;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Products group
pbx_groups.append(f"""\t\t{group_products_ref} /* Products */ = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{product_ref} /* irpotify.app */,
\t\t\t);
\t\t\tname = Products;
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Main root group
pbx_groups.append(f"""\t\t{group_root_ref} = {{
\t\t\tisa = PBXGroup;
\t\t\tchildren = (
\t\t\t\t{group_irpotify_ref} /* irpotify */,
\t\t\t\t{group_products_ref} /* Products */,
\t\t\t);
\t\t\tsourceTree = "<group>";
\t\t}};""")

# Sources build phase
sources_entries = [f"\t\t\t\t{build_files[f][0]} /* {os.path.basename(f)} in Sources */," for f in swift_files]
sources_phase = f"""\t\t{sources_phase_ref} /* Sources */ = {{
\t\t\tisa = PBXSourcesBuildPhase;
\t\t\tbuildActionMask = 2147483647;
\t\t\tfiles = (
{chr(10).join(sources_entries)}
\t\t\t);
\t\t\trunOnlyForDeploymentPostprocessing = 0;
\t\t}};"""

# Resources build phase
res_entries = [f"\t\t\t\t{build_files[r][0]} /* {os.path.basename(r)} in Resources */," for r in resource_files]
resources_phase = f"""\t\t{resources_phase_ref} /* Resources */ = {{
\t\t\tisa = PBXResourcesBuildPhase;
\t\t\tbuildActionMask = 2147483647;
\t\t\tfiles = (
{chr(10).join(res_entries)}
\t\t\t);
\t\t\trunOnlyForDeploymentPostprocessing = 0;
\t\t}};"""

# Frameworks phase
frameworks_phase = f"""\t\t{frameworks_phase_ref} /* Frameworks */ = {{
\t\t\tisa = PBXFrameworksBuildPhase;
\t\t\tbuildActionMask = 2147483647;
\t\t\tfiles = (
\t\t\t);
\t\t\trunOnlyForDeploymentPostprocessing = 0;
\t\t}};"""

# Native target
native_target = f"""\t\t{target_ref} /* irpotify */ = {{
\t\t\tisa = PBXNativeTarget;
\t\t\tbuildConfigurationList = {target_cfg_list_ref} /* Build configuration list for PBXNativeTarget "irpotify" */;
\t\t\tbuildPhases = (
\t\t\t\t{sources_phase_ref} /* Sources */,
\t\t\t\t{frameworks_phase_ref} /* Frameworks */,
\t\t\t\t{resources_phase_ref} /* Resources */,
\t\t\t);
\t\t\tbuildRules = (
\t\t\t);
\t\t\tdependencies = (
\t\t\t);
\t\t\tname = irpotify;
\t\t\tproductName = irpotify;
\t\t\tproductReference = {product_ref} /* irpotify.app */;
\t\t\tproductType = "com.apple.product-type.application";
\t\t}};"""

# Build Configurations
project_configs = f"""\t\t{proj_cfg_debug_ref} /* Debug */ = {{
\t\t\tisa = XCBuildConfiguration;
\t\t\tbuildSettings = {{
\t\t\t\tALWAYS_SEARCH_USER_PATHS = NO;
\t\t\t\tCLANG_ANALYZER_NONNULL = YES;
\t\t\t\tCLANG_ANALYZER_NUMBER_OBJECT_CONVERSION = YES_AGGRESSIVE;
\t\t\t\tCLANG_CXX_LANGUAGE_STANDARD = "gnu++20";
\t\t\t\tCLANG_ENABLE_MODULES = YES;
\t\t\t\tCLANG_ENABLE_OBJC_ARC = YES;
\t\t\t\tCLANG_ENABLE_OBJC_WEAK = YES;
\t\t\t\tCLANG_WARN_BLOCK_CAPTURE_AUTORELEASING = YES;
\t\t\t\tCLANG_WARN_BOOL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_COMMA = YES;
\t\t\t\tCLANG_WARN_CONSTANT_CONVERSION = YES;
\t\t\t\tCLANG_WARN_DEPRECATED_OBJC_IMPLEMENTATIONS = YES;
\t\t\t\tCLANG_WARN_DIRECT_OBJC_ISA_USAGE = YES_ERROR;
\t\t\t\tCLANG_WARN_DOCUMENTATION_COMMENTS = YES;
\t\t\t\tCLANG_WARN_EMPTY_BODY = YES;
\t\t\t\tCLANG_WARN_ENUM_CONVERSION = YES;
\t\t\t\tCLANG_WARN_INFINITE_RECURSION = YES;
\t\t\t\tCLANG_WARN_INT_CONVERSION = YES;
\t\t\t\tCLANG_WARN_NON_LITERAL_NULL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_OBJC_IMPLICIT_RETAIN_SELF = YES;
\t\t\t\tCLANG_WARN_OBJC_LITERAL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_OBJC_ROOT_CLASS = YES_ERROR;
\t\t\t\tCLANG_WARN_QUOTED_INCLUDE_IN_FRAMEWORK_HEADER = YES;
\t\t\t\tCLANG_WARN_RANGE_LOOP_ANALYSIS = YES;
\t\t\t\tCLANG_WARN_STRICT_PROTOTYPES = YES;
\t\t\t\tCLANG_WARN_SUSPICIOUS_MOVE = YES;
\t\t\t\tCLANG_WARN_UNGUARDED_AVAILABILITY = YES_AGGRESSIVE;
\t\t\t\tCLANG_WARN_UNREACHABLE_CODE = YES;
\t\t\t\tCLANG_WARN__DUPLICATE_METHOD_MATCH = YES;
\t\t\t\tCOPY_PHASE_STRIP = NO;
\t\t\t\tDEBUG_INFORMATION_FORMAT = dwarf;
\t\t\t\tENABLE_STRICT_OBJC_MSGSEND = YES;
\t\t\t\tENABLE_TESTABILITY = YES;
\t\t\t\tGCC_C_LANGUAGE_STANDARD = gnu11;
\t\t\t\tGCC_DYNAMIC_NO_PIC = NO;
\t\t\t\tGCC_NO_COMMON_BLOCKS = YES;
\t\t\t\tGCC_OPTIMIZATION_LEVEL = 0;
\t\t\t\tGCC_PREPROCESSOR_DEFINITIONS = (
\t\t\t\t\t"DEBUG=1",
\t\t\t\t\t"$(inherited)",
\t\t\t\t);
\t\t\t\tGCC_WARN_64_TO_32_BIT_CONVERSION = YES;
\t\t\t\tGCC_WARN_ABOUT_RETURN_TYPE = YES_ERROR;
\t\t\t\tGCC_WARN_UNDEFINED_VARIABLES = YES;
\t\t\t\tGCC_WARN_UNUSED_FUNCTION = YES;
\t\t\t\tGCC_WARN_UNUSED_VARIABLE = YES;
\t\t\t\tIPHONEOS_DEPLOYMENT_TARGET = 16.0;
\t\t\t\tMTL_ENABLE_DEBUG_INFO = INCLUDE_SOURCE;
\t\t\t\tMTL_FAST_MATH = YES;
\t\t\t\tONLY_ACTIVE_ARCH = YES;
\t\t\t\tSDKROOT = iphoneos;
\t\t\t\tSWIFT_ACTIVE_COMPILATION_CONDITIONS = DEBUG;
\t\t\t\tSWIFT_OPTIMIZATION_LEVEL = "-Onone";
\t\t\t}};
\t\t\tname = Debug;
\t\t}};
\t\t{proj_cfg_release_ref} /* Release */ = {{
\t\t\tisa = XCBuildConfiguration;
\t\t\tbuildSettings = {{
\t\t\t\tALWAYS_SEARCH_USER_PATHS = NO;
\t\t\t\tCLANG_ANALYZER_NONNULL = YES;
\t\t\t\tCLANG_ANALYZER_NUMBER_OBJECT_CONVERSION = YES_AGGRESSIVE;
\t\t\t\tCLANG_CXX_LANGUAGE_STANDARD = "gnu++20";
\t\t\t\tCLANG_ENABLE_MODULES = YES;
\t\t\t\tCLANG_ENABLE_OBJC_ARC = YES;
\t\t\t\tCLANG_ENABLE_OBJC_WEAK = YES;
\t\t\t\tCLANG_WARN_BLOCK_CAPTURE_AUTORELEASING = YES;
\t\t\t\tCLANG_WARN_BOOL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_COMMA = YES;
\t\t\t\tCLANG_WARN_CONSTANT_CONVERSION = YES;
\t\t\t\tCLANG_WARN_DEPRECATED_OBJC_IMPLEMENTATIONS = YES;
\t\t\t\tCLANG_WARN_DIRECT_OBJC_ISA_USAGE = YES_ERROR;
\t\t\t\tCLANG_WARN_DOCUMENTATION_COMMENTS = YES;
\t\t\t\tCLANG_WARN_EMPTY_BODY = YES;
\t\t\t\tCLANG_WARN_ENUM_CONVERSION = YES;
\t\t\t\tCLANG_WARN_INFINITE_RECURSION = YES;
\t\t\t\tCLANG_WARN_INT_CONVERSION = YES;
\t\t\t\tCLANG_WARN_NON_LITERAL_NULL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_OBJC_IMPLICIT_RETAIN_SELF = YES;
\t\t\t\tCLANG_WARN_OBJC_LITERAL_CONVERSION = YES;
\t\t\t\tCLANG_WARN_OBJC_ROOT_CLASS = YES_ERROR;
\t\t\t\tCLANG_WARN_QUOTED_INCLUDE_IN_FRAMEWORK_HEADER = YES;
\t\t\t\tCLANG_WARN_RANGE_LOOP_ANALYSIS = YES;
\t\t\t\tCLANG_WARN_STRICT_PROTOTYPES = YES;
\t\t\t\tCLANG_WARN_SUSPICIOUS_MOVE = YES;
\t\t\t\tCLANG_WARN_UNGUARDED_AVAILABILITY = YES_AGGRESSIVE;
\t\t\t\tCLANG_WARN_UNREACHABLE_CODE = YES;
\t\t\t\tCLANG_WARN__DUPLICATE_METHOD_MATCH = YES;
\t\t\t\tCOPY_PHASE_STRIP = NO;
\t\t\t\tDEBUG_INFORMATION_FORMAT = "dwarf-with-dsym";
\t\t\t\tENABLE_NS_ASSERTIONS = NO;
\t\t\t\tENABLE_STRICT_OBJC_MSGSEND = YES;
\t\t\t\tGCC_C_LANGUAGE_STANDARD = gnu11;
\t\t\t\tGCC_NO_COMMON_BLOCKS = YES;
\t\t\t\tGCC_WARN_64_TO_32_BIT_CONVERSION = YES;
\t\t\t\tGCC_WARN_ABOUT_RETURN_TYPE = YES_ERROR;
\t\t\t\tGCC_WARN_UNDEFINED_VARIABLES = YES;
\t\t\t\tGCC_WARN_UNUSED_FUNCTION = YES;
\t\t\t\tGCC_WARN_UNUSED_VARIABLE = YES;
\t\t\t\tIPHONEOS_DEPLOYMENT_TARGET = 16.0;
\t\t\t\tMTL_ENABLE_DEBUG_INFO = NO;
\t\t\t\tMTL_FAST_MATH = YES;
\t\t\t\tSDKROOT = iphoneos;
\t\t\t\tSWIFT_COMPILATION_MODE = "wholemodule";
\t\t\t\tSWIFT_OPTIMIZATION_LEVEL = "-O";
\t\t\t\tVALIDATE_PRODUCT = YES;
\t\t\t}};
\t\t\tname = Release;
\t\t}};"""

target_configs = f"""\t\t{target_cfg_debug_ref} /* Debug */ = {{
\t\t\tisa = XCBuildConfiguration;
\t\t\tbuildSettings = {{
\t\t\t\tASSETCATALOG_COMPILER_APPICON_NAME = AppIcon;
\t\t\t\tASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME = AccentColor;
\t\t\t\tCODE_SIGN_STYLE = Manual;
\t\t\t\tCODE_SIGNING_ALLOWED = NO;
\t\t\t\tCODE_SIGNING_REQUIRED = NO;
\t\t\t\tCURRENT_PROJECT_VERSION = 1;
\t\t\t\tENABLE_PREVIEWS = YES;
\t\t\t\tGENERATE_INFOPLIST_FILE = NO;
\t\t\t\tINFOPLIST_FILE = irpotify/Resources/Info.plist;
\t\t\t\tIPHONEOS_DEPLOYMENT_TARGET = 16.0;
\t\t\t\tLD_RUNPATH_SEARCH_PATHS = (
\t\t\t\t\t"$(inherited)",
\t\t\t\t\t"@executable_path/Frameworks",
\t\t\t\t);
\t\t\t\tMARKETING_VERSION = 1.0.0;
\t\t\t\tPRODUCT_BUNDLE_IDENTIFIER = com.irpotify.mobile;
\t\t\t\tPRODUCT_NAME = "$(TARGET_NAME)";
\t\t\t\tSWIFT_EMIT_LOC_STRINGS = YES;
\t\t\t\tSWIFT_VERSION = 5.0;
\t\t\t\tTARGETED_DEVICE_FAMILY = "1,2";
\t\t\t}};
\t\t\tname = Debug;
\t\t}};
\t\t{target_cfg_release_ref} /* Release */ = {{
\t\t\tisa = XCBuildConfiguration;
\t\t\tbuildSettings = {{
\t\t\t\tASSETCATALOG_COMPILER_APPICON_NAME = AppIcon;
\t\t\t\tASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME = AccentColor;
\t\t\t\tCODE_SIGN_STYLE = Manual;
\t\t\t\tCODE_SIGNING_ALLOWED = NO;
\t\t\t\tCODE_SIGNING_REQUIRED = NO;
\t\t\t\tCURRENT_PROJECT_VERSION = 1;
\t\t\t\tENABLE_PREVIEWS = YES;
\t\t\t\tGENERATE_INFOPLIST_FILE = NO;
\t\t\t\tINFOPLIST_FILE = irpotify/Resources/Info.plist;
\t\t\t\tIPHONEOS_DEPLOYMENT_TARGET = 16.0;
\t\t\t\tLD_RUNPATH_SEARCH_PATHS = (
\t\t\t\t\t"$(inherited)",
\t\t\t\t\t"@executable_path/Frameworks",
\t\t\t\t);
\t\t\t\tMARKETING_VERSION = 1.0.0;
\t\t\t\tPRODUCT_BUNDLE_IDENTIFIER = com.irpotify.mobile;
\t\t\t\tPRODUCT_NAME = "$(TARGET_NAME)";
\t\t\t\tSWIFT_EMIT_LOC_STRINGS = YES;
\t\t\t\tSWIFT_VERSION = 5.0;
\t\t\t\tTARGETED_DEVICE_FAMILY = "1,2";
\t\t\t}};
\t\t\tname = Release;
\t\t}};"""

# Configuration lists
config_lists = f"""\t\t{proj_cfg_list_ref} /* Build configuration list for PBXProject "irpotify" */ = {{
\t\t\tisa = XCConfigurationList;
\t\t\tbuildConfigurations = (
\t\t\t\t{proj_cfg_debug_ref} /* Debug */,
\t\t\t\t{proj_cfg_release_ref} /* Release */,
\t\t\t);
\t\t\tdefaultConfigurationIsVisible = 0;
\t\t\tdefaultConfigurationName = Release;
\t\t}};
\t\t{target_cfg_list_ref} /* Build configuration list for PBXNativeTarget "irpotify" */ = {{
\t\t\tisa = XCConfigurationList;
\t\t\tbuildConfigurations = (
\t\t\t\t{target_cfg_debug_ref} /* Debug */,
\t\t\t\t{target_cfg_release_ref} /* Release */,
\t\t\t);
\t\t\tdefaultConfigurationIsVisible = 0;
\t\t\tdefaultConfigurationName = Release;
\t\t}};"""

# PBXProject
pbx_project = f"""\t\t{project_obj_ref} /* Project object */ = {{
\t\t\tisa = PBXProject;
\t\t\tattributes = {{
\t\t\t\tBuildIndependentTargetsInParallel = 1;
\t\t\t\tLastSwiftUpdateCheck = 1500;
\t\t\t\tLastUpgradeCheck = 1500;
\t\t\t\tTargetAttributes = {{
\t\t\t\t\t{target_ref} = {{
\t\t\t\t\t\tCreatedOnToolsVersion = 15.0;
\t\t\t\t\t}};
\t\t\t\t}};
\t\t\t}};
\t\t\tbuildConfigurationList = {proj_cfg_list_ref} /* Build configuration list for PBXProject "irpotify" */;
\t\t\tcompatibilityVersion = "Xcode 14.0";
\t\t\tdevelopmentRegion = en;
\t\t\thasScannedForEncodings = 0;
\t\t\tknownRegions = (
\t\t\t\ten,
\t\t\t\tBase,
\t\t\t);
\t\t\tmainGroup = {group_root_ref};
\t\t\tproductRefGroup = {group_products_ref} /* Products */;
\t\t\tprojectDirPath = "";
\t\t\tprojectRoot = "";
\t\t\ttargets = (
\t\t\t\t{target_ref} /* irpotify */,
\t\t\t);
\t\t}};"""

# Assemble project.pbxproj content
project_content = f"""// !$*UTF8*$!
{{
\tarchiveVersion = 1;
\tclasses = {{
\t}};
\tobjectVersion = 56;
\tobjects = {{

/* Begin PBXBuildFile section */
{chr(10).join(pbx_build_file)}
/* End PBXBuildFile section */

/* Begin PBXFileReference section */
{chr(10).join(pbx_file_ref)}
/* End PBXFileReference section */

/* Begin PBXFrameworksBuildPhase section */
{frameworks_phase}
/* End PBXFrameworksBuildPhase section */

/* Begin PBXGroup section */
{chr(10).join(pbx_groups)}
/* End PBXGroup section */

/* Begin PBXNativeTarget section */
{native_target}
/* End PBXNativeTarget section */

/* Begin PBXProject section */
{pbx_project}
/* End PBXProject section */

/* Begin PBXResourcesBuildPhase section */
{resources_phase}
/* End PBXResourcesBuildPhase section */

/* Begin PBXSourcesBuildPhase section */
{sources_phase}
/* End PBXSourcesBuildPhase section */

/* Begin XCBuildConfiguration section */
{project_configs}
{target_configs}
/* End XCBuildConfiguration section */

/* Begin XCConfigurationList section */
{config_lists}
/* End XCConfigurationList section */

\t}};
\trootObject = {project_obj_ref} /* Project object */;
}}
"""

with open(os.path.join(xcodeproj_dir, "project.pbxproj"), "w", encoding="utf-8") as f:
    f.write(project_content)

# Shared scheme
xcscheme_content = f"""<?xml version="1.0" encoding="UTF-8"?>
<Scheme
   LastUpgradeVersion = "1500"
   version = "1.7">
   <BuildAction
      parallelizeBuildables = "YES"
      buildImplicitDependencies = "YES">
      <BuildActionEntries>
         <BuildActionEntry
            buildForTesting = "YES"
            buildForRunning = "YES"
            buildForProfiling = "YES"
            buildForArchiving = "YES"
            buildForAnalyzing = "YES">
            <BuildableReference
               BuildableIdentifier = "primary"
               BlueprintIdentifier = "{target_ref}"
               BuildableName = "irpotify.app"
               BlueprintName = "irpotify"
               ReferencedContainer = "container:irpotify.xcodeproj">
            </BuildableReference>
         </BuildActionEntry>
      </BuildActionEntries>
   </BuildAction>
   <TestAction
      buildConfiguration = "Debug"
      selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
      selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
      shouldUseLaunchSchemeArgsEnv = "YES"
      shouldAutocreateTestPlan = "YES">
   </TestAction>
   <LaunchAction
      buildConfiguration = "Debug"
      selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
      selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
      launchStyle = "0"
      useCustomWorkingDirectory = "NO"
      ignoresPersistentStateOnLaunch = "NO"
      debugDocumentVersioning = "YES"
      debugServiceExtension = "internal"
      allowLocationSimulation = "YES">
      <BuildableProductRunnable
         runnableDebuggingMode = "0">
         <BuildableReference
            BuildableIdentifier = "primary"
            BlueprintIdentifier = "{target_ref}"
            BuildableName = "irpotify.app"
            BlueprintName = "irpotify"
            ReferencedContainer = "container:irpotify.xcodeproj">
         </BuildableReference>
      </BuildableProductRunnable>
   </LaunchAction>
   <ProfileAction
      buildConfiguration = "Release"
      shouldUseLaunchSchemeArgsEnv = "YES"
      savedToolIdentifier = ""
      useCustomWorkingDirectory = "NO"
      debugDocumentVersioning = "YES">
      <BuildableProductRunnable
         runnableDebuggingMode = "0">
         <BuildableReference
            BuildableIdentifier = "primary"
            BlueprintIdentifier = "{target_ref}"
            BuildableName = "irpotify.app"
            BlueprintName = "irpotify"
            ReferencedContainer = "container:irpotify.xcodeproj">
         </BuildableReference>
      </BuildableProductRunnable>
   </ProfileAction>
   <AnalyzeAction
      buildConfiguration = "Debug">
   </AnalyzeAction>
   <ArchiveAction
      buildConfiguration = "Release"
      revealArchiveInOrganizer = "YES">
   </ArchiveAction>
</Scheme>
"""

with open(os.path.join(xcscheme_dir, "irpotify.xcscheme"), "w", encoding="utf-8") as f:
    f.write(xcscheme_content)

print("Generated irpotify.xcodeproj successfully!")
