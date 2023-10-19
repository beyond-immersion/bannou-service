#!/bin/bash

LIBS_DIR="libs"
TARGET_FRAMEWORK="net7.0"
rm -rf $LIBS_DIR
mkdir $LIBS_DIR

projects=($(find . -name '*.csproj'))
for proj in "${projects[@]}"
do
    namespace=$(dotnet build $proj --configuration Release -nologo -v:q -t:PrintServiceLib)

    if [ $? -ne 0 ]; then
        echo "Skipping project $proj: not a service library."
    elif [[ ! -z "$namespace" ]]; then
        echo "Copying libs for service $namespace."

        source_dir="$LIBS_DIR/$(dirname $proj)/bin/Release/$TARGET_FRAMEWORK"
        target_dir="$LIBS_DIR/$namespace"
        mkdir -p "$target_dir"

        cp -r "$source_dir/"* "$target_dir/"
    fi
done
