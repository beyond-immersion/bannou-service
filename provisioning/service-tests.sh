docker exec -it provisioning_bannou_1 bash
response=$(curl --write-out '%{http_code}' --output /dev/null 'localhost/testing/run/basic')
exit
echo "API response:" response
if [ response == 200 ]; then
    exit
fi
exit 1
