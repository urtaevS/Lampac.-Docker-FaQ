#!/usr/bin/env bash
DEST="/home/lampac"
cd $DEST

ver=$(cat vers.txt)
gitver=$(curl -s https://api.github.com/repos/immisterio/Lampac/releases/latest | grep tag_name | sed s/[^0-9]//g)
if [ $gitver -gt $ver ]; then
    systemctl stop lampac
    echo "update lampac to version $gitver ..."
    rm -f update.zip
    curl -L -o update.zip https://github.com/immisterio/Lampac/releases/latest/download/update.zip
    unzip -o update.zip
    rm -f update.zip
    echo -n $gitver > vers.txt
    systemctl start lampac
else
    echo "lampac already current version $ver"
fi
