#!/bin/bash

# Check if xmllint is installed
if ! command -v xmllint &> /dev/null; then
    echo "xmllint could not be found, and is required for building service libs. Skipping..."
    exit 1
fi

LIBS_DIR="libs"
TARGET_FRAMEWORK="net7.0"
rm -rf $LIBS_DIR
mkdir $LIBS_DIR

projects=($(find . -name '*.csproj'))
for proj in "${projects[@]}"
do
    project_dir="$(dirname $proj)"
    project_name="$(basename $project_dir)"

    namespace=$(xmllint --xpath "Project/PropertyGroup/ServiceLib/text()" $proj)

    if [ $? -ne 0 ]; then
        echo "Skipping project '$project_name': not a service library."

    elif [[ ! -z "$namespace" ]]; then

        source_dir="$project_name/bin/Release/$TARGET_FRAMEWORK"
        target_dir="$LIBS_DIR/$namespace"

        echo "Building libs for project '$project_name'."
        dotnet build $proj --configuration Release -nologo --verbosity quiet

        echo "Moving project libs to '$target_dir' directory."
        mkdir -p "$target_dir"
        cp -r "$source_dir/"* "$target_dir/"
    fi
done
