#!/bin/bash

LIBS_DIR="libs"
TARGET_FRAMEWORK="net7.0"
rm -rf $LIBS_DIR
mkdir $LIBS_DIR

projects=($(find . -name '*.csproj'))
for proj in "${projects[@]}"
do
    namespace=$(dotnet build $proj --configuration Release -nologo -v:q -t:PrintServiceLib)
    echo "Copying libs for service $namespace..."

    if [[ ! -z "$namespace" ]]; then
        target_dir="$LIBS_DIR/$namespace"
        mkdir -p "$target_dir"

        cp "$(dirname $proj)/bin/Release/$TARGET_FRAMEWORK/"* "$target_dir/"
    fi
done
