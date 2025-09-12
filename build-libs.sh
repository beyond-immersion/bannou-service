#!/bin/bash

# Check if required commands are installed
commands=("xmllint" "rsync")
for cmd in "${commands[@]}"; do
    if ! command -v $cmd &> /dev/null; then
        echo "$cmd could not be found, and is required for building service libs. Skipping..."
        exit 1
    fi
done

LIBS_DIR="libs"
TARGET_FRAMEWORK="net9.0"

mkdir -p $LIBS_DIR

projects=($(find . -name '*.csproj'))
for proj in "${projects[@]}"
do
    project_dir="$(dirname $proj)"
    project_name="$(basename $project_dir)"

    namespace=$(xmllint --xpath "Project/PropertyGroup/ServiceLib/text()" $proj)

    if [ $? -ne 0 ]; then
        echo "Skipping project '$project_name': not a service library."

    elif [[ ! -z "$namespace" ]]; then

        source_dir="$project_name/bin/Release/$TARGET_FRAMEWORK/publish"

        echo "Building libs for project '$project_name'."
        dotnet publish $proj --configuration Release --no-self-contained -nologo --verbosity quiet -p:GenerateNewServices=false -p:GenerateUnitTests=false

        #echo "Files in '$source_dir' to be copied to '$LIBS_DIR' directory:"
        #ls "$source_dir"

        rsync -a "$source_dir/" "$LIBS_DIR/"
    fi
done
