docker exec -i cl-bannou-1 bash
response=$(curl --write-out '%{http_code}' --silent --output /dev/null 'http://127.0.0.1/testing/run')
exit
echo response
